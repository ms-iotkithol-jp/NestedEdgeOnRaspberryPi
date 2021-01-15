using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
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
            if (args.Length == 0)
            {
                Console.WriteLine("Command line:");
                Console.WriteLine("dotnet run iothub-device-connection-string [IOTEDGE_TRUSTED_CA_CERTIFICATE_PEM_PATH]");
                return;
            }
            
            string iothubcs = args[0];
            Console.WriteLine($"Connection string of IoT Hub Device:{iothubcs}");
            if (args.Length == 2)
            {
                string trustedCACertPath = args[1];
                InstallCACert(trustedCACertPath);
                Console.WriteLine($"Installed {trustedCACertPath} as Certficate");
            }

            DoWork(iothubcs).Wait();
        }

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

                var key = Console.ReadKey();

                await deviceClient.CloseAsync();
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Exception {ex.Message}");
            }
        }

        private static void ConnectionStatusChangeHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            Console.WriteLine($"ConnectionStatus Changed - status={status},reason={reason}");
        }

        private static async Task ReceiveMessageHandler(Message message, object userContext)
        {
            Console.WriteLine($"Recieved - {System.Text.Encoding.UTF8.GetString(message.GetBytes())}");
            foreach(var prp in message.Properties)
            {
                Console.WriteLine($" {prp.Key}:{prp.Value}");
            }
            var deviceClient = (DeviceClient)userContext;
            await deviceClient.CompleteAsync(message);
        }

        private static async Task<MethodResponse> MethodHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"Invoked - {methodRequest.Name}({methodRequest.DataAsJson})");
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
            Console.WriteLine($"Updated - Desired Properties={dpJson}");
            var reported = new TwinCollection();
            reported["Desired"] = new TwinCollection(dpJson);
            await deviceClient.UpdateReportedPropertiesAsync(reported);
        }

        static void InstallCACert(string trustedCACertPath)
        {
            if (!string.IsNullOrWhiteSpace(trustedCACertPath))
            {
                Console.WriteLine("User configured CA certificate path: {0}", trustedCACertPath);
                if (!File.Exists(trustedCACertPath))
                {
                    // cannot proceed further without a proper cert file
                    Console.WriteLine("Certificate file not found: {0}", trustedCACertPath);
                    throw new InvalidOperationException("Invalid certificate file.");
                }
                else
                {
                    Console.WriteLine("Attempting to install CA certificate: {0}", trustedCACertPath);
                    X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(new X509Certificate2(X509Certificate.CreateFromCertFile(trustedCACertPath)));
                    Console.WriteLine("Successfully added certificate: {0}", trustedCACertPath);
                    store.Close();
                }
            }
            else
            {
                Console.WriteLine("CA_CERTIFICATE_PATH was not set or null, not installing any CA certificate");
            }
        }
    }
}
