# IoT Edge Module 内での IoT Device 制御  

## Azure IoT Edge Module Build と Deploy
root.ca.cert.pem ファイルを、MultiIoTDeviceAppsInGatewayEdge/IoTDeviceAppInGatewayEdge にコピーする。  
MultiIoTDeviceAppsInGatewayEdge ディレクトリで、以下を実行する。  
```
$ sudo docker build -t iotdevapplauncher -f Dockerfile.arm32v7 .
$ sudo docker tag iotdevapplauncher youracrserver/iotdevapplauncher:1.0.0-arm32v7
$ sudo docker push youracrserver/iotdevapplauncher:1.0.0-arm32v7
```
※ <i><b>youracrserver</b></i> は各自の Azure Container Registry の URL、もしくは、Docker Hub のユーザーアカウント名   

Azure Portal で、Azure IoT Edge Module として Deploy 指定する。Container Create Options を以下の様に設定する。  
```json
{
  "Config": {
    "OpenStdin": true
  }
}
```
※ 子プロセスの Standard Input、Standard Output を使って親プロセスと通信するために必要な設定らしいよ。  

## IoT Device のプロビジョニング  
Deploy した Module の Direct Method を Invoke してIoT Device をプロビジョニングする。  

関数名： Start  
Payload：
```json
{
  "LaunchCommand": "./iotapp/IoTDeviceAppForGatewayEdge",
  "HostName": "youriothub.azure-devices.net",
  "DeviceId": "nested-child-app-1",
  "SharedAccessKey": "該当する Shared Access Key",
  "GatewayHostName": "IoT Edge が動いている Raspberry Pi の IP アドレス",
  "TrustedCACertPath": "./iotapp/azure-iot-test-only.root.ca.cert.pem"
}
```
Stop という Direct Method をコールすると、停止＆削除  
