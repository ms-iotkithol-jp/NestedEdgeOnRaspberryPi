// #define USE_PIPELINE_STREAM
// IoT Edge Module として起動すると、親プロセスとの間でStandard Input、Standard Outputのテキスト受け渡しができなかったので、
// とりあえず、Named Pipeを試してみたが、Standard Input、Standard Outputは、Create Optionsで、OpenStdionをtrueに設定すると使えるようになったので、
// そちらを採用。悪戦苦闘の記録として一応残しておく

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.IO.Pipes;

namespace IoTDeviceAppLauncher
{
    public class IoTDeviceAppProcess
    {
        private Process myProcess;
        private ProcessStartInfo startInfo;

        public static readonly string HostNameArgKey = "HostName";
        public static readonly string DeviceIdArgKey = "DeviceId";
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
        public string TargetDeviceId { get; set; }

        public StreamReader myStandardOutput { get { return myProcess.StandardOutput; } }
        public StreamReader myStandardError { get { return myProcess.StandardError; } }
  
        
        // commandName = "dotnet run iotdevapp.dll"
        public IoTDeviceAppProcess(string launchName, string trustedCACertPath)
        {
            this.launchName = launchName;
            this.trustedCACertPath = trustedCACertPath;
            IsStarted = false;
        }

        public Process Create(string argsJson)
        {
            dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(argsJson);
            string hostName = "";
            string sharedAccessKey = "";
            if (json.HostName != null) hostName = json.HostName;
            if (json.SharedAccessKey != null) sharedAccessKey = json.SharedAccessKey;
            TargetDeviceId = json.DeviceId;

            string connectionString = $"\"{HostNameArgKey}={hostName};{DeviceIdArgKey}={TargetDeviceId};{SharedAccessKeyArgKey}={sharedAccessKey}";
            if (json.GatewayHostName is null)
            {
                connectionString += "\"";
            }
            else
            {
                connectionString += $";{GatewayHostNameArgKey}={json.GatewayHostName}\"";
            }
            string commandName = launchName;
            
            string args = $"{connectionString} {trustedCACertPath}";
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
            Console.WriteLine($"[{TargetDeviceId}]error - ");
            Console.WriteLine(e.Data);
        }

        private void MyProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine($"[{TargetDeviceId}]output - ");
            Console.WriteLine(e.Data);
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
                    Console.WriteLine($"[{TargetDeviceId}] ${output}");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"ReceiveProcOutput:Exception - {ex.Message}");
            }
        }
    }
}
