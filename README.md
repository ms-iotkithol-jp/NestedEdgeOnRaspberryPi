# Nested Edge を Raspberry Pi で試す 
[Azure IoT Edge](https://docs.microsoft.com/ja-jp/azure/iot-edge/about-iot-edge?view=iotedge-2020-11) に2020年末に、Azure IoT Edge を Transparent Gateway として使い、Child Device として IoT Edge を接続できるようになった、Nested Edge という機能を、その筋の開発者なら大抵持っている、Raspbery Pi（3以上）で試すための Tips 集。  
OS は、現在最新の Raspbian Buster を使用する。  

※ 2021/1/8時点で、一応「[ダウンストリーム IoT Edge デバイスを Azure IoT Edge ゲートウェイに接続する (プレビュー)](https://docs.microsoft.com/ja-jp/azure/iot-edge/how-to-connect-downstream-iot-edge-device?view=iotedge-2020-11&tabs=azure-portal)」という Docs の説明はあるものの、多分これを参照してもうまく設定できないと思われるので、実際に動かせたステップを紹介する。  

※ 以下の説明は、「[Azure IoT Edge Runtime 1.2 新機能のご紹介 - Nested Edge](https://qiita.com/motoJinC25/items/1650813c465d2c58591c)」という素晴らしい技術ブログを元に、Raspberry Pi、Raspbian でやる場合に躓きそうな部分説明する。  

---
## 内容  
Nested Edge は、Raspberry Pi を２セット利用する。それぞれ、以下の hostname 、IP アドレスであると仮定して話を進める。また、Raspberry Pi を接続するローカルネットで、DNS が利用できない場合は、Static IP Address を付与してやるべきではあるが、DHCP でも一定期間同じ IP アドレスで接続されるので、それを活用する。２セットあるので混乱しないように作業を進めてほしい。  
|種類|hostname|IP Address|
|-|-|-|
親デバイス|raspi-parent|192.168.0.108
|子デバイス|raspi-child|192.168.0.155

手順は、以下の通り。  
1. ホスト名の設定  
2. Raspbian Buster への IoT Edge Preview 版インストール
3. 親デバイスと子デバイスの接続用の証明書作成と、親デバイスへの証明書インストール
4. 親デバイスへの、Deploy 設定と、config.yaml の設定
5. Azure IoT Device SDK を使った IoT アプリの接続テスト(寄り道)
6. 子デバイスへの証明書インストール 
7. 子デバイスへの、Deploy 設定と、config.yaml の設定、Nested Edge の動作確認 
8. IoT Edge Module 内で、Device SDK アプリを起動し、そのアプリを 子デバイスとして扱う。
-----
## ホスト名の設定  
Raspbian のインストールが終わったら、以下の二つのファイルの <b>raspberrypi</b> を親、子、それぞれの名前に変更する。  
- /etc/hostname
- /etc/hosts  

変更が終わったら、Reboot して確定  
※ 別に変更しなくてもいいんだが、Putty 等で Remote Shell 上で作業する場合に区別がついてよいので推奨  



---
## Raspbian Buster への IoT Edge Preview 版インストール  
実は、2021/1/8 現在、Raspbian 対応は正式には、Stretch までで、Buster には正式対応していない。でも動く。  
この作業は、親、子、両方で行う。
まずは、Docker をインストール  
```shell
curl -sSL get.docker.com | sh && sudo usermod pi -aG docker && sudo reboot
```
一旦リブートするので、リブート後に、以下を実行。
```shell
curl -L https://github.com/Azure/azure-iotedge/releases/download/1.2.0-rc1/libiothsm-std_1.2.0_rc1-1-1_debian9_armhf.deb
 -o libiothsm-std.deb
sudo dpkg -i ./libiothsm-std.deb
curl -L https://github.com/Azure/azure-iotedge/releases/download/1.2.0-rc1/iotedge_1.2.0_rc1-1_debian9_armhf.deb
 -o iotedge.deb
sudo dpkg -i ./iotedge.deb
```
※ Nested Edge は、Preview 版の[1.2.0-RC1](https://github.com/Azure/azure-iotedge/releases/tag/1.2.0-rc1)を使用  
※ Busterhは、Debian 10 だが、まぁいいだろう。

---
## 親デバイスと子デバイスの接続用の証明書作成と 親デバイスへの証明書インストール  
冒頭に紹介した技術ブログの「[証明書とコピー - 親エッジデバイス](https://qiita.com/motoJinC25/items/1650813c465d2c58591c#%E8%A6%AA%E3%82%A8%E3%83%83%E3%82%B8%E3%83%87%E3%83%90%E3%82%A4%E3%82%B9-1)」に記載された方法で、子側の Raspberry Pi が親側の Raspberry Pi に接続するための証明書の作成と、親側の Raspberry Pi への証明書のインストールを行う。  
※ 証明書のインストールの部分で、
```
ルートCA証明書インストール
sudo cp $HOME/azure-iot-test-only.root.ca.cert.pem /usr/local/share/ca-certificates/azure-iot-test-only.root.ca.cert.pem.crt
sudo update-ca-certificates
```
/usr/local/share/ca-certificates にコピーする時に拡張子が変わっていることに注意。

---
## 親デバイスへの、Deploy 設定と、config.yaml の設定  
親側(raspi-parent)の、IoT Edge の設定を行う。冒頭に紹介したブログの「[IoT Edge Runtime 構成 - 親エッジデバイス](https://qiita.com/motoJinC25/items/1650813c465d2c58591c#%E8%A6%AA%E3%82%A8%E3%83%83%E3%82%B8%E3%83%87%E3%83%90%E3%82%A4%E3%82%B9-2)」に従って、raspi-parent の /etc/iotedge/config.yaml を編集する。    
前述の IP アドレスの構成の場合、Raspberry Pi での実行なので、  
```yaml
certificates:
  device_ca_cert: "/home/pi/iot-edge-device-ca-parent-device-full-chain.cert.pem"
  device_ca_pk: "/home/pi/iot-edge-device-ca-parent-device.key.pem"
  trusted_ca_certs: "/home/pi/azure-iot-test-only.root.ca.cert.pem"
```  
```yaml
hostname: "192.168.0.108"
```
となる。  
次に、「[親エッジデバイスへのモジュールデプロイ](https://qiita.com/motoJinC25/items/1650813c465d2c58591c#%E8%A6%AA%E3%82%A8%E3%83%83%E3%82%B8%E3%83%87%E3%83%90%E3%82%A4%E3%82%B9%E3%81%B8%E3%83%A2%E3%82%B8%E3%83%A5%E3%83%BC%E3%83%AB%E3%83%87%E3%83%97%E3%83%AD%E3%82%A4)」に従って、Azure Portal で、raspi-parent にモジュールのデプロイ設定を行う。  
※ 長い文字列が多いので、可能な限りブログからコピペして、スペルミスによるお間抜けな失敗をしないようにすること。  

Azure Portal での設定後、しばらくして、raspi-parent のシェル上で、指定したモジュールが配置され動いていることが確認できる。  
```shell
pi@raspi-parent:~ $ sudo iotedge list
NAME             STATUS           DESCRIPTION      CONFIG
IoTEdgeAPIProxy  running          Up 21 minutes    mcr.microsoft.com/azureiotedge-api-proxy:latest
edgeAgent        running          Up a day         mcr.microsoft.com/azureiotedge-agent:1.2.0-rc3
edgeHub          running          Up 21 minutes    mcr.microsoft.com/azureiotedge-hub:1.2.0-rc3
registry         running          Up 21 minutes    registry:latest
```

---
## Azure IoT Device SDK を使った IoT アプリの接続テスト(寄り道)  
ここで、若干横道にそれて、Azure IoT Device SDK を使ったアプリを、raspi-parent で動作する親 IoT Edge を介して、Azure IoT Hub に接続してみる。  
※ この機能は、元々あった機能である。  
アプリは、[IoTDeviceAppForGatewayEdge](./IoTDeviceAppForGatewayEdge) を使う。このアプリは、.NET Core 上で実行するアプリなので、まず、.NET Core SDK を raspi-parent にインストールする。2021/1/8 現在、.NET Core の最新バージョンは 5.0 なのでそれを使う。  
```shell
pi@raspi-parent:~ $ curl -L https://download.visualstudio.microsoft.com/download/pr/567a64a8-810b-4c3f-85e3-bc9f9e06311b/02664afe4f3992a4d558ed066d906745/dotnet-sdk-5.0.101-linux-arm.tar.gz
 -o dotent-sdk.tar.gz
pi@raspi-parent:~ $ sudo mkdir -p $HOME/dotnet
pi@raspi-parent:~ $ sudo tar zxf dotnet-sdk.tar.gz -C $HOME/dotnet
pi@raspi-parent:~ $ export DOTNET_ROOT=$HOME/dotnet
pi@raspi-parent:~ $ export PATH=$PATH:$HOME/dotnet
```
pi ユーザーでログインする度に、.NET Core の環境が有効になるために、$HOME/.bashrc の最後に、
```
export DOTNET_ROOT=$HOME/dotnet
export PATH=$PATH:$HOME/dotnet
```
の二行を追加する。  
このリポジトリを clone する。
```shell
pi@raspi-parent:~ $ git clone https://github.com/ms-iotkithol-jp/NestedEdgeOnRaspberryPi.git
pi@raspi-parent:~ $ cd NestedEdge/IoTDeviceAppForGatewayEdge
```
Azure Portal で、IoT Device に、leaf-device という名前でデバイスを登録し、接続文字列をコピーし、その接続文字列に以下の様に Gateway 情報を加える
```
HostName=<myiothub>.azure-devices.net;DeviceId=leaf-device;SharedAccessKey=xxxyyyzzz;GatewayHostName=192.168.0.108
```
最後の IP アドレスは、raspi-parent の IP アドレスである。 
この接続文字列を""で囲って引数として使い、アプリを起動する。  
```
$ dotnet run "HostName=...;GatewayHostName=192.168.0.108"
```
※ ""を必ずつけること
IoTDeviceAppForGatewayEdge は、10回、Azure IoT Hub にメッセージを送信し、Shell 空のキー入力待ちになる。このアプリへの C2D メッセージ送信、Device Twins Desired Properties 更新、Direct Method コールが全てできるので、試してみること。
※ このアプリは、Visual Studio のリモートデバッガーのアタッチを想定している。利用しない場合は、Program.cs の１行目をコメントアウトすること。  
一見、IoT Hub に直接接続しているようにみえるが、raspi-parent の IoT Edge Runtime を介しての接続・通信になっている。  


次に、.NET Core SDK のインストールと同じアプリの実行を raspi-child 側でも試してみる。  
pi@raspi-child:~/NestedEdgeOnRaspberryPi/IoTDeviceAppForGatewayEdge $ dotnet run "HostName=<myiothub>.azure-devices.net;DeviceId=leaf-device;SharedAccessKey=xxxxxxxxyyyyyyyy;GatewayHostName=192.168.0.108"
waiting for debugger attach
```
waiting for debugger attach
Connection string of IoT Hub Device:HostName=...;DeviceId=leaf-device;SharedAccessKey=xxxxxxxxyyyyyyyy;GatewayHostName=192.168.0.108
Exception TLS authentication error.
```
当然であるが、証明書のインストールをしていなければ、raspi-parent とは接続できないので、実行は失敗する。

---
## 子デバイスへの証明書インストール  
前述の、親デバイス側で作成して子デバイス側にコピーした証明書をインストールする。「[証明書作成とコピー - 子デバイス](https://qiita.com/motoJinC25/items/1650813c465d2c58591c#%E5%AD%90%E3%82%A8%E3%83%83%E3%82%B8%E3%83%87%E3%83%90%E3%82%A4%E3%82%B9-1)」に従ってインストールする。  
※ 親デバイスのパートでも書いたが、こちらも、/usr/local/share/ca-certificates にコピーする時の拡張子の違いに注意すること。  
証明書のインストールが終わったら、前のステップで行った、IoTDeviceAppForGatewayEdge をもう一度実行してみよう。今度は raspi-parent への接続が成功し、Azure IoT Hub へのメッセージ送信、コマンド等の送受信が問題なく実行されるので、試してみてね。  
以上で、寄り道は終わり。

---

## 子デバイスへの、Deploy 設定と、config.yaml の設定、Nested Edge の動作確認  
寄り道して、IoT Edge ではない子デバイスの接続も体験したので、次に本題の IoT Edge を子デバイスとしての接続を試す。  
raspi-child の /etc/iotedge/config.yaml を、「[IoT Edge Runtime 構成 - 子エッジデバイス](https://qiita.com/motoJinC25/items/1650813c465d2c58591c#%E5%AD%90%E3%82%A8%E3%83%83%E3%82%B8%E3%83%87%E3%83%90%E3%82%A4%E3%82%B9-2)」に従って編集する。  
Raspberry Pi の場合、かつ、IP アドレスの場合について、念のため以下に設定を記載する。
```yaml
certificates:
  device_ca_cert: "/home/pi/iot-edge-device-ca-child-device-full-chain.cert.pem"
  device_ca_pk: "/home/pi/iot-edge-device-ca-child-device.key.pem"
  trusted_ca_certs: "/home/pi/azure-iot-test-only.root.ca.cert.pem"
```  
```yaml
hostname: "192.168.0.150"
```
```yaml
parent_hostname: "192.168.0.108"
```
```yaml
agent:
  name: "edgeAgent"
  type: "docker"
  env: {}
  config:
    image: "192.168.0.108:8000/azureiotedge-agent:1.2.0-rc3"
    auth: {}
```
config.yaml の編集が終わったら、sudo systemctl restart iotedge で IoT Edge Runtime を再起動する。しばらく経つと、SimulatedTemperatureSensor からのテレメトリーデータ送信が確認できる。
sudo iotedge list で試すと必要なモジュールがきちんと Pull されて実行できているのが確認できる。  
```shell
pi@raspi-child:~ $ sudo iotedge list
NAME                        STATUS           DESCRIPTION      CONFIG
SimulatedTemperatureSensor  running          Up an hour       192.168.0.108:8000/azureiotedge-simulated-temperature-sensor:1.0
edgeAgent                   running          Up an hour       192.168.0.108:8000/azureiotedge-agent:1.2.0-rc3
edgeHub                     running          Up an hour       192.168.0.108:8000/azureiotedge-hub:1.2.0-rc3
```
以上で、Nested Edge の確認は終了。 

---
## IoT Edge Module 内で、Device SDK アプリを起動し、そのアプリを 子デバイスとして扱う  
IoT Edge の Transparent Gateway としての利用は、ローカルネット側で様々な通信プロトコルで通信しているデバイス群を IoT Hub に接続する Protocol Gateway の実現方法として活用される。  
このような場合、プロトコルで通信しているデバイス群をそれぞれ IoT Device として IoT Hub に登録し、IoT Edge Module 内で、それぞれの IoT Device 用のプロセスを作って、そのプロセス内で、登録された各 IoT Device 用の接続文字列を使って、IoT Edge デバイスを介して IoT Hub に接続し双方向通信を行う、といった実装が可能である。  
このサンプルでは、そのような実装方法のひな型を提供する。  
詳しくは、[IoT Edge Module 内での IoT Device 制御](MultiIoTDeviceMgmtInIoTEdgeModule.md)を参照の事。
