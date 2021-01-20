// #define USE_PIPELINE_STREAM
// IoT Edge Module として起動すると、親プロセスとの間でStandard Input、Standard Outputのテキスト受け渡しができなかったので、
// とりあえず、Named Pipeを試してみたが、Standard Input、Standard Outputは、Create Optionsで、OpenStdionをtrueに設定すると使えるようになったので、
// そちらを採用。悪戦苦闘の記録として一応残しておく
//#define DESKTOP_TEST

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.IO.Pipes;
using Microsoft.Azure.Devices.Client;

namespace IoTDeviceAppLauncher
{
    public class IoTDeviceAppProcess
    {
        private Process myProcess;
        private ProcessStartInfo startInfo;

        public static readonly string HostNameArgKey = "HostName";
        public static readonly string DeviceIdArgKey = "DeviceId";
        public static readonly string TargetDeviceIdArgKey = "TargetId";
        public static readonly string SharedAccessKeyArgKey = "SharedAccessKey";
        public static readonly string GatewayHostNameArgKey = "GatewayHostName";

#if USE_PIPELINE_STREAM
        static string appInputPipelineStreamName = "iotDevAppInputPipelinStream";
        static string appOutputPipelineStreamName = "iotDevAppOutputPipelineStream";
        NamedPipeClientStream procPipeClientOutputStream;
        NamedPipeClientStream procPipeClientInputStream;
        StreamReader procStreamReader;

#endif
        StreamWriter procStreamWriter;

        private string launchName;
        private string trustedCACertPath;
        public bool IsStarted { get; set; }
        public string IoTHubDeviceId { get; set; }
        public string TargetDeviceId { get; set; }

        public StreamReader myStandardOutput { get { return myProcess.StandardOutput; } }
        public StreamReader myStandardError { get { return myProcess.StandardError; } }
  
        
        // commandName = "dotnet run iotdevapp.dll"
        public IoTDeviceAppProcess(string launchName, string trustedCACertPath, string targetDeviceId, string iothubDeviceId)
        {
            this.launchName = launchName;
            this.trustedCACertPath = trustedCACertPath;
            TargetDeviceId = targetDeviceId;
            IoTHubDeviceId = iothubDeviceId;
            IsStarted = false;
        }
#if DESKTOP_TEST
#else
        public ModuleClient ParentModuleClient { get; set; }
        public string EdgeHubMessageOutputName { get; set; }
        public static readonly string StdOutC2DMessageKey = "Recieved:";
        public static readonly string StdOutDirectMethodKey = "Invoked:";
        public static readonly string StdOutDesiredPropsKey = "Updated:";
#endif

        public Process Create(string argsJson)
        {
            dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(argsJson);
            string hostName = "";
            string sharedAccessKey = "";
            if (json.HostName != null) hostName = json.HostName;
            if (json.SharedAccessKey != null) sharedAccessKey = json.SharedAccessKey;

            string connectionString = $"\"{HostNameArgKey}={hostName};{DeviceIdArgKey}={IoTHubDeviceId};{SharedAccessKeyArgKey}={sharedAccessKey}";
            if (json.GatewayHostName is null)
            {
                connectionString += "\"";
            }
            else
            {
                connectionString += $";{GatewayHostNameArgKey}={json.GatewayHostName}\"";
            }
            string commandName = launchName;
            
            string args = $"{connectionString} {TargetDeviceId} {trustedCACertPath}";
            startInfo = new ProcessStartInfo(commandName);
            startInfo.Arguments = args;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardError = true;

            myProcess = new Process()
            {
                StartInfo = startInfo
            };
            myProcess.OutputDataReceived += MyProcess_OutputDataReceived;
            myProcess.ErrorDataReceived += MyProcess_ErrorDataReceived;

            return myProcess;
        }

        public async Task SendMessage(byte[] message)
        {
            var msgChars = System.Text.Encoding.UTF8.GetChars(message);
            Console.WriteLine($"Message Sending:{System.Text.Encoding.UTF8.GetString(message)}");
            await procStreamWriter.WriteLineAsync(msgChars);
        }

        private void MyProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine($"[{IoTHubDeviceId}]error - ");
            Console.WriteLine(e.Data);
        }

        private void MyProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine($"[{IoTHubDeviceId}]output - ");
            Console.WriteLine(e.Data);
#if DESKTOP_TEST
#else
            string message = "";
            string msgType = "";
            if (e.Data.StartsWith(StdOutC2DMessageKey))
            {
                message = e.Data.Substring(StdOutC2DMessageKey.Length);
                msgType = "c2d";
            }
            else if (e.Data.StartsWith(StdOutDesiredPropsKey))
            {
                message = e.Data.Substring(StdOutDirectMethodKey.Length);
                msgType = "dm";
            }
            else if (e.Data.StartsWith(StdOutDesiredPropsKey))
            {
                message = e.Data.Substring(StdOutDesiredPropsKey.Length);
                msgType = "dp";
            }
            if (!string.IsNullOrEmpty(msgType))
            {
                var msgContent = new
                {
                    targetid = TargetDeviceId,
                    msgtype = msgType,
                    content = message
                };
                var msgContentJson = Newtonsoft.Json.JsonConvert.SerializeObject(msgContent);
                Console.WriteLine($"Send to {EdgeHubMessageOutputName} - '{msgContentJson}'");
                ParentModuleClient.SendEventAsync(EdgeHubMessageOutputName, new Message(System.Text.Encoding.UTF8.GetBytes(msgContentJson))).Wait();
            }
#endif
        }

        public async Task<bool> Start()
        {
            if (myProcess != null && IsStarted == false)
            {
                IsStarted = myProcess.Start();
#if USE_PIPELINE_STREAM
                procPipeClientInputStream = new NamedPipeClientStream(".",appOutputPipelineStreamName, PipeDirection.In);
                procPipeClientInputStream.Connect();
                procPipeClientOutputStream = new NamedPipeClientStream(".", appInputPipelineStreamName, PipeDirection.Out);
                procPipeClientOutputStream.Connect();
                procStreamReader = new StreamReader(procPipeClientInputStream);
                procStreamWriter = new StreamWriter(procPipeClientOutputStream);

                ReceiveProcOutput(procStreamReader);
#else
                myProcess.BeginOutputReadLine();
                myProcess.BeginErrorReadLine();
                procStreamWriter = myProcess.StandardInput;
#endif
            }
            return IsStarted;
        }

        public async Task<bool> Stop()
        {
            bool result = true;
            if (IsStarted)
            {
                await procStreamWriter.WriteAsync("q");
#if USE_PIPELINE_STREAM
                procPipeClientInputStream.Close();
                procPipeClientOutputStream.Close();
#endif
                myProcess.WaitForExit();
                myProcess.Close();
            }
            else
            {
                result = false;
            }
            return result;
        }

        async Task ReceiveProcOutput(StreamReader reader)
        {
            try
            {
                while (true)
                {
                    string output = await reader.ReadLineAsync();
                    Console.WriteLine($"[{IoTHubDeviceId}] ${output}");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"ReceiveProcOutput:Exception - {ex.Message}");
            }
        }
    }
}
