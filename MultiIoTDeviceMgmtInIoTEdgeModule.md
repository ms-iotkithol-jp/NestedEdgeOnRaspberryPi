# IoT Edge Module 内での IoT Device 制御  
Transparent Gateway を使った、Protocol Gateway のひな型サンプルを紹介する。  
業界標準プロトコルで通信しあう既存の設備機器や、HWレベルのワイヤード接続されたデバイス群、Bluetooth通信しているデバイス群があるとする。それら設備機器やデバイスを適度な粒度で分割し、それぞれを Azure IoT Hub に IoT Device として登録し、それぞれのプロトコルで相互通信している設備機器・デバイス群と Azure IoT Edge 上で動いている Azure IoT Edge Module がそれぞれのプロトコルで通信し、その Azure IoT Edge Module 内で、Azure IoT Hub との送受信の形式に双方向変換してやれば、独自プロトコルで相互通信している機器・デバイス群と、クラウド上のサービス間で双方向通信が可能な基盤を容易に構築できる。  
加えて、それぞれの設備機器・デバイスに対し、管理用のメタデータや制御設定データを、Device Twins を用いた、保持・更新の通知・参照する仕組みも容易に構築できる。追加のDBやFunctions等が不必要なので、運用コスト面でも有利である。  
更に、IoT Edge の Transparent Gateway 機構を使って、各設備機器・デバイスに対応する IoT Device と Azure IoT Hub の通信は、Azure IoT Edge を Gateway として集約されるため、物理回線上の、接続数も1つで済む。  
Protocol 変換ロジックは、当然ながら、それぞれの Protocol 毎に異なるロジックを用意しなければならないが、複数の設備機器・デバイスが IoT Device として IoT Hub と相互通信する部分は、パターン化できる。  
そのようなパターン化した IoT Edge Module のサンプルを提供する。構成は以下の通り。

![overview](images/IoTDevAppInGWOverview.png)


---
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

## IoTDevAppLauncher の関数  
Deploy した Module の Direct Method を Invoke してIoT Device をプロビジョニングする。  
関数名： Start  
Payload：
```json
{
  "TargetId":"ext1",
  "LaunchCommand": "./iotapp/IoTDeviceAppForGatewayEdge",
  "HostName": "youriothub.azure-devices.net",
  "DeviceId": "nested-child-app-1",
  "SharedAccessKey": "該当する Shared Access Key",
  "GatewayHostName": "IoT Edge が動いている Raspberry Pi の IP アドレス",
  "TrustedCACertPath": "./iotapp/azure-iot-test-only.root.ca.cert.pem"
}
```
|パラメータ|意味|
|-|-|
|TargetId|IoT Hub 未対応のプロトコルで通信しているデバイスの名前|
|LaunchCommand|TargetId のデバイスを Azure IoT Hub に登録された IoT Device として扱うためのアプリケーションコマンド|
|HostName|接続先の Azure IoT Hub の URL|
|DeviceId|Azure IoT Hub 上の登録された Device Id|
|ShareAccessKey|DeviceId の Shared Access Key|
|GatewayHostName|Azure IoT Edge のホストの IP アドレス、または、DNS上のFQDN名|
|TrustedCACertPath|IoT Edge Gateway で Downstream デバイスを接続するための証明書|
この関数をコールするごとに対応するデバイスとして Azure IoT Hub と通信するプロセスを、本モジュール内で起動する。  

---
関数名: Stop  
Payload：
```json
{
  "TargetId":"ext1"
}
```
Start で起動したプロセスを停止＆削除する。
|パラメータ|意味|
|-|-|
|TargetId|Start で指定した TargetId|

---
関数名: SendMessageToIoTDevApp  
Payload:
```json
{
    "TargetId":"ext1",
    "type":"d2c",
    "message":{"message":"test","value":2}}
```
|パラメータ|意味|
|-|-|
|TargetId|Start で指定した TargetId|
|type|メッセージの種別|
|message|メッセージの本体|
テスト用の関数。messageInput の代替。  
- type が "d2c" の場合、DeviceId からの Azure IoTHub への Device 2 Cloud メッセージが送信される
- type が "urp" の場合、DeviceId の Reported Properties が更新される。

---
## 別プロトコル側と IoTDeviceAppForGatewayEdge との相互通信  
IoTDeviceAppLauncer のメッセージ入出力を使って、別プロトコル側と Azure IoT Hub 側での、Identity 変換と双方向通信を行う。  
### messageInput 
messageInput を介して他の Azure IoT Edge Module からデータを受信し、IoTDeviceAppforGatewayEdge プロセスにメッセージを転送する。  
これにより、Azure IoT Hub へのメッセージ送信、Reported Properties の更新が行われる。  
データフォーマット：  
```
type:message
```
- type が "d2c" の場合、DeviceId からの Azure IoTHub への Device 2 Cloud メッセージが送信される
- type が "urp" の場合、DeviceId の Reported Properties が更新される
type が urp の場合は、messageの部分は、JSON形式のテキストでなければならない。d2c の場合は、JSON 形式でなくても構わない。  
送信するメッセージには、"target" をキー、値を別プロトコル側の識別子（Start の TargetId）とするプロパティを付与すること。

### messageOutput
Azure IoT Hub から IoTDeviceAppforGatewayEdge プロセスに送信された Device 2 Cloud メッセージ、Desired Properties の更新、Direct Method のコールに関する情報が出力される。  
別プロトコル側の Azure IoT Edge Module はこのアウトプットからメッセージを受信すること。  
メッセージフォーマットは、以下の通り
```json
{
    "targetid":"ext1",
    "msgtype":"c2d",
    "message":"{"message":"hello"}"
}
```
|プロパティ|意味|
|-|-|
|targetid|Start で指定した TargetId|
|msgtype|メッセージの種別|
|message|メッセージの本体|
msgtype の種別は以下の通り。  
|種別|意味|
|-|-|
|c2d|Azure IoT Hub からの Cloud 2 Devic メッセージ|
|dm|Direct Method コール|
|dp|Desired Properties 更新| 
IoTDeviceAppLauncher モジュールと別プロトコル側を扱う Azure IoT Edge Module の間のやり取りは、全て、別プロセス側のデバイスのアイデンティティで行う。

---
## お試し用 Docker Image 
|実行環境|Image Uri|
|-|-|
|Raspberry Pi|embeddedgeorge/iotdevapplauncher:1.0.0-arm32v7|
|Windows 10|embeddedgeorge/iotdevapplauncher:1.0.0-windows-amd64
