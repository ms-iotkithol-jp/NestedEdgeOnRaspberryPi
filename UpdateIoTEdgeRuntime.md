# Azure IoT Edge Runtime V1.2.0-rc3 以前 から V1.2.0 以降へのアップデート  
2021/3/1 に、1.2.0-rc4 がリリースされた際、IoT Edge Runtime の構成・設定方法が大幅に変わってしまっている。  
既に正式リリース版の 1.2.0 も 4/10 にリリースされているので、遅ればせながら既存ユーザー向けに、IoT Edge Runtime の 1.2.0 へのアップデート方法を解説する。  
作業手順は以下の通り。  
1. Azure IoT Edge Runtime の停止  
2. 1.2.0-rc3 以前のインストール済みの Runtime Library をアンインストール  
3. 1.2.0 の Runtime Library 群をインストール  
4. 以前の設定ファイルを最新の形式に移行する  

## 1. Azure IoT Edge Runtime の停止  
以下のコマンドで停止  
```sh
sudo systemctl stop iotedge
```

## 2. 1.2.0-rc3 以前のインストール済みの Runtime Library をアンインストール  
以下のコマンドでインストール済みの前バージョン用のライブラリをアンインストールする。
```sh
sudo apt-get remove libiothsm-std
sudo apt-get remove aziot-edge
```

## 3. 1.2.0 の Runtime Library 群をインストール  
まず、インストールしたいバージョンの deb ファイルの URL を確認する。  
ブラウザで、[https://github.com/Azure/azure-iotedge/releases](https://github.com/Azure/azure-iotedge/releases) を開き、使いたいバージョンのリンクをクリックする。  
開いたページの下の方に、deb ファイルの一覧があるので、Raspbian Buster の場合は、Debian 10, ARM32なので、  
- [aziot-edge_1.2.0-1_debian10_armhf.deb](https://github.com/Azure/azure-iotedge/releases/download/1.2.0/aziot-edge_1.2.0-1_debian10_armhf.deb)
- [aziot-identity-service_1.2.0-1_debian10_armhf.deb](https://github.com/Azure/azure-iotedge/releases/download/1.2.0/aziot-identity-service_1.2.0-1_debian10_armhf.deb)  

の2つをダウンロードしてインストールすることになる。  
```sh
curl -L https://github.com/Azure/azure-iotedge/releases/download/1.2.0/aziot-edge_1.2.0-1_debian10_armhf.deb -o aziot-edge.deb
curl -L https://github.com/Azure/azure-iotedge/releases/download/1.2.0/aziot-identity-service_1.2.0-1_debian10_armhf.deb -o aziot-identity-service.deb
```

ダウンロードした二つのファイルをインストールする。
```sh
sudo dpkg -i ./aziot-identity-service.deb
sudo dpkg -i ./aziot-edge.deb
```
インストールの順番に注意。  
aziot-edge.deb をインストールすると、既に旧バージョンがインストールされていた場合は、それを検知し、旧バージョンの設定をインポートする方法が表示されるので、基本、それに従って作業を行えばよい。  


## 4. 以前の設定ファイルを最新の形式に移行する  
aziot-edge.deb インストール時に表示された内容に従い、  
```sh
sudo iotedge config import
```
を実行すると、旧バージョンの設定 /etc/iotedge/config.yaml の内容を元に、 /etc/aziot/config.toml が生成される。  
生成されたファイルの中身を見れば、新形式の設定方法が一目瞭然なので、しばし、じっくり眺めてみることをお勧めする。  
次に、
```sh
sudo iotedge config apply
```
を実行すると、新 IoT Edge の設定が置き換わりリスタートされる。  
以上で、IoT Edge Runtime のアップデートは完了だが、IoT Edge Module の配置指定で、IoT Edge Runtime Module（edgeAgent, EdgeHub）のバージョンも更新してあげないと動かないのでご注意。  


## 補足  
MQTT Broker on IoT Edge 向けの配置指定は、未だに Azure Portal が対応していないようなので、引き続き、Azure CLI による配置指定を行う必要があるようだ at 2021/5/28
