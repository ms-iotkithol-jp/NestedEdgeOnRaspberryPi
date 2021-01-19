//#define REMOTE_DEBUG
//#define TEST
//#define USE_PIPELINE_STREAM
// IoT Edge Module として起動すると、親プロセスとの間でStandard Input、Standard Outputのテキスト受け渡しができなかったので、
// とりあえず、Named Pipeを試してみたが、Standard Input、Standard Outputは、Create Optionsで、OpenStdionをtrueに設定すると使えるようになったので、
// そちらを採用。悪戦苦闘の記録として一応残しておく

using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace IoTDeviceAppForGatewayEdge
{
    class Program
    {
        static void Main(string[] args)
        {
#if (REMOTE_DEBUG)
            // for remote debug attaching
            for (; ; )
            {
                Console.WriteLine("waiting for debugger attach");
                if (Debugger.IsAttached) break;
                System.Threading.Thread.Sleep(1000);
            }
#endif
#if USE_PIPELINE_STREAM
            AppInputStream = new NamedPipeServerStream(appInputPipelineStreamName, PipeDirection.In);
            AppOutputStream = new NamedPipeServerStream(appOutputPipelineStreamName, PipeDirection.Out);
            AppInputStream.WaitForConnection();
            AppOutputStream.WaitForConnection();
            AppStreamReader = new StreamReader(AppInputStream);
            AppStreamWriter = new StreamWriter(AppOutputStream);
#else
            AppStreamReader = Console.In;
            AppStreamWriter = Console.Out;
#endif

            if (args.Length == 0)
            {
                AppStreamWriter.WriteLine("log:Command line:");
                AppStreamWriter.WriteLine("log:dotnet run iothub-device-connection-string [IOTEDGE_TRUSTED_CA_CERTIFICATE_PEM_PATH]");
                return;
            }
            
            string iothubcs = args[0];
            AppStreamWriter.WriteLine($"log:Connection string of IoT Hub Device:{iothubcs}");
            if (args.Length == 2)
            {
                string trustedCACertPath = args[1];
                InstallCACert(trustedCACertPath);
                AppStreamWriter.WriteLine($"log:Installed {trustedCACertPath} as Certficate");
            }

            DoWork(iothubcs).Wait();

#if USE_PIPELINE_STREAM
            AppInputStream.Close();
            AppOutputStream.Close();
#endif
        }

#if USE_PIPELINE_STREAM
        static string appInputPipelineStreamName = "iotDevAppInputPipelinStream";
        static string appOutputPipelineStreamName = "iotDevAppOutputPipelineStream";

        static NamedPipeServerStream AppInputStream { get; set; }
        static NamedPipeServerStream AppOutputStream { get; set; }
#endif
        static TextReader AppStreamReader { get; set; }
        static TextWriter AppStreamWriter { get; set; }

        static async Task DoWork(string iothubCS)
        {
            DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(iothubCS);
            try
            {
                string deviceId = iothubCS.Split(";")[1].Split("=")[1];

                deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangeHandler);
                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateHandler,deviceClient);
                await deviceClient.SetMethodDefaultHandlerAsync(MethodHandler, deviceClient);
                await deviceClient.SetReceiveMessageHandlerAsync(ReceiveMessageHandler, deviceClient);

                await deviceClient.OpenAsync();
#if TEST
                int count = 10;
                for(int i = 0; i < count; i++)
                {
                    var msgContent = new
                    {
                        deviceId = deviceId,
                        timestamp = DateTime.UtcNow,
                        count = i
                    };
                    var msgJson = Newtonsoft.Json.JsonConvert.SerializeObject(msgContent);
                    var msg = new Message(System.Text.Encoding.UTF8.GetBytes(msgJson));
                    await deviceClient.SendEventAsync(msg);
                    Console.WriteLine($"Send - {msgJson}");
                    await Task.Delay(5000);
                }
#endif
                while (true)
                {
                    var input = AppStreamReader.ReadLine().Trim();
                    if (input.ToLower().StartsWith("quit"))
                    {
                        AppStreamWriter.WriteLine("log:Quit");
                        break;
                    }
                    else
                    {
                        string d2cMessageKey = "d2c:";
                        string urpMessageKey = "urp:";
                        if (input.StartsWith(d2cMessageKey))
                        {
                            var msg = new Message(System.Text.Encoding.UTF8.GetBytes(input.Substring(d2cMessageKey.Length)));
                            AppStreamWriter.WriteLine($"log:Send D2C - {input}");
                            await deviceClient.SendEventAsync(msg);
                        }
                        else if (input.StartsWith(urpMessageKey))
                        {
                            var reported = new TwinCollection();
                            AppStreamWriter.WriteLine($"log:Update RP - {input}");
                            reported["External"] = new TwinCollection(input.Substring(urpMessageKey.Length));
                            await deviceClient.UpdateReportedPropertiesAsync(reported);
                        }
                    }
                }

                
                await deviceClient.CloseAsync();
            }
            catch(Exception ex)
            {
                AppStreamWriter.WriteLine($"log:Exception {ex.Message}");
            }
        }

        private static void ConnectionStatusChangeHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            AppStreamWriter.WriteLine($"log:ConnectionStatus Changed - status={status},reason={reason}");
        }

        private static async Task ReceiveMessageHandler(Message message, object userContext)
        {
            var recvMsg = new
            {
                properties = message.Properties,
                message = System.Text.Encoding.UTF8.GetString(message.GetBytes())
            };
            AppStreamWriter.WriteLine($"Recieved:{Newtonsoft.Json.JsonConvert.SerializeObject(recvMsg)}");
            var deviceClient = (DeviceClient)userContext;
            await deviceClient.CompleteAsync(message);
        }

        private static async Task<MethodResponse> MethodHandler(MethodRequest methodRequest, object userContext)
        {
            AppStreamWriter.WriteLine($"Invoked:{methodRequest.Name}({methodRequest.DataAsJson})");
            var payload = new
            {
                message = "OK"
            };
            var response = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(payload)), 200);
            return response;
        }

        private static async Task DesiredPropertyUpdateHandler(TwinCollection desiredProperties, object userContext)
        {
            var deviceClient = (DeviceClient)userContext;
            var dpJson = desiredProperties.ToJson();
            AppStreamWriter.WriteLine($"Updated:{dpJson}");
        }

        static void InstallCACert(string trustedCACertPath)
        {
            if (!string.IsNullOrWhiteSpace(trustedCACertPath))
            {
                AppStreamWriter.WriteLine("log:User configured CA certificate path: {0}", trustedCACertPath);
                if (!File.Exists(trustedCACertPath))
                {
                    // cannot proceed further without a proper cert file
                    AppStreamWriter.WriteLine("log:Certificate file not found: {0}", trustedCACertPath);
                    throw new InvalidOperationException("Invalid certificate file.");
                }
                else
                {
                    AppStreamWriter.WriteLine("log:Attempting to install CA certificate: {0}", trustedCACertPath);
                    X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(new X509Certificate2(X509Certificate.CreateFromCertFile(trustedCACertPath)));
                    AppStreamWriter.WriteLine("log:Successfully added certificate: {0}", trustedCACertPath);
                    store.Close();
                }
            }
            else
            {
                AppStreamWriter.WriteLine("log:CA_CERTIFICATE_PATH was not set or null, not installing any CA certificate");
            }
        }
    }
}
