//#define REMOTE_DEBUG
//#define DESKTOP_TEST

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Azure.Devices.Client;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.Loader;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;

namespace IoTDeviceAppLauncher
{
    class Program
    {
#if DESKTOP_TEST
        static DeviceClient deviceClient;
#else
        static ModuleClient moduleClient;
#endif
        static CancellationTokenSource cancellationTokenSource;

        static void Main(string[] args)
        {
            Console.WriteLine("IoTDeviceAppLauncer");
#if (REMOTE_DEBUG)
            // for remote debug attaching
            int i = 0;
            for (; ; )
            {
                Console.WriteLine($"waiting for debugger attach {i++}");
                if (Debugger.IsAttached) break;
                System.Threading.Thread.Sleep(1000);
            }
#endif

#if DESKTOP_TEST
            deviceClient = DeviceClient.CreateFromConnectionString(args[0]);
            deviceClient.SetMethodDefaultHandlerAsync(directMethodHandler, deviceClient).Wait();
            deviceClient.OpenAsync().Wait();
            Console.WriteLine("IoT Hub Connected.");
#else
            Init().Wait();
            Console.WriteLine("Edge Runtime Connected.");
#endif

            cancellationTokenSource = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => { cancellationTokenSource.Cancel(); };
            Console.CancelKeyPress += (sender, cpe) => { cancellationTokenSource.Cancel(); };
#if DESKTOP_TEST
            while (true)
            {
                var input = Console.ReadLine().Trim();
                if (input.ToLower() == "quit")
                {
                    break;
                }
                ProcessInput(input).Wait();
            }
#endif
            WhenCancelled(cancellationTokenSource.Token).Wait();
            foreach (var pkey in launchedIoTDevProcs.Keys)
            {
                launchedIoTDevProcs[pkey].Stop().Wait();
            }
#if DESKTOP_TEST
            deviceClient.CloseAsync().Wait();
#else
#endif
        }

#if DESKTOP_TEST
#else
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await moduleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            await moduleClient.SetMethodDefaultHandlerAsync(directMethodHandler, moduleClient);
            await moduleClient.SetInputMessageHandlerAsync("messageInput", inputMessageHandler, moduleClient);
        }

        // message for D2C : should be "d2c:..."
        // message for Update Reported Properties  : should be "urp:"
        private static async Task<MessageResponse> inputMessageHandler(Message message, object userContext)
        {
            var target = message.Properties["target"];
            if (launchedIoTDevProcs.ContainsKey(target))
            {
                await launchedIoTDevProcs[target].SendMessage(message.GetBytes());
            }
            return MessageResponse.Completed;
        }
#endif 
        static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        static async Task ProcessInput(string input)
        {
            var index = input.IndexOf(":");
            if (index > 0)
            {
                var deviceId = input.Substring(0, index);
                var message = input.Substring(index + 1);
                if (launchedIoTDevProcs.ContainsKey(deviceId))
                {
                    await launchedIoTDevProcs[deviceId].SendMessage(System.Text.Encoding.UTF8.GetBytes(message));
                }
            }
        }

        static Dictionary<string, IoTDeviceAppProcess> launchedIoTDevProcs = new Dictionary<string, IoTDeviceAppProcess>();
        private static async Task<MethodResponse> directMethodHandler(MethodRequest methodRequest, object userContext)
        {
            string resultPayload = "";
            int resultStatus = 200;
            Console.WriteLine($"Invoked - {methodRequest.Name}({methodRequest.DataAsJson})");
            dynamic payloadJson = Newtonsoft.Json.JsonConvert.DeserializeObject(methodRequest.DataAsJson);
            string appDeviceId = "";
            if (payloadJson.DeviceId != null) appDeviceId = payloadJson.DeviceId;
            if (appDeviceId != null)
            {
                try
                {
                    switch (methodRequest.Name)
                    {
                        case "Start":
                            string launchCommand = "";
                            string trustedCACertPath = "";
                            if (payloadJson.LaunchCommand != null) launchCommand = payloadJson.LaunchCommand;
                            if (payloadJson.TrustedCACertPath != null) trustedCACertPath = payloadJson.TrustedCACertPath;
                            if (launchCommand != null && trustedCACertPath != null)
                            {
                                var iotAppProc = new IoTDeviceAppProcess(launchCommand, trustedCACertPath);
#if DESKTOP_TEST
#else
                                iotAppProc.EdgeHubMessageOutputName = "messageOutput";
                                iotAppProc.ParentModuleClient = moduleClient;
#endif
                                var p = iotAppProc.Create(methodRequest.DataAsJson);
                                launchedIoTDevProcs.Add(appDeviceId, iotAppProc);
                                var started = await iotAppProc.Start();
                                if (started)
                                {
                                    resultPayload = "Created and Started";
                                    resultStatus = 201;
                                }
                                else
                                {
                                    resultPayload = "Failed";
                                    resultStatus = 202;
                                }
                            }
                            else
                            {
                                resultPayload = "Bad Request";
                                resultStatus = 404;
                            }
                            break;
                        case "Stop":
                            if (launchedIoTDevProcs.ContainsKey(appDeviceId))
                            {
                                var iotDevProc = launchedIoTDevProcs[appDeviceId];
                                await iotDevProc.Stop();
                                launchedIoTDevProcs.Remove(appDeviceId);
                                resultPayload = "Stopped";
                            }
                            else
                            {
                                resultPayload = "The process for specified device id doesn't exist.";
                                resultStatus = 202;
                            }
                            break;
                        case "SendMessageToIoTDevApp":
                            if (launchedIoTDevProcs.ContainsKey(appDeviceId))
                            {
                                var iotDevProc = launchedIoTDevProcs[appDeviceId];
                                dynamic toMsg = payloadJson["message"];
                                string msg = Newtonsoft.Json.JsonConvert.SerializeObject(toMsg);
                                await iotDevProc.SendMessage(System.Text.Encoding.UTF8.GetBytes(msg));
                                resultPayload = $"Sent msg({msg}) to {appDeviceId}";
                            }
                            break;
                        default:
                            resultPayload = "Bad Request";
                            resultStatus = 404;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    resultPayload = $"Exception - {ex.Message}";
                }
            }
            else
            {
                resultPayload = "Bad Request";
                resultStatus = 404;
            }
            var responsePayload = new
            {
                message = resultPayload
            };
            var response = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(responsePayload)), resultStatus);
            return response;
        }

        static async Task<Process> LaunchAsync(string file, string args)
        {
            var processInfo = new ProcessStartInfo(file);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.Arguments = args;

            var p = Process.Start(processInfo);

            return p;
        }
    }
}
