# 第三者サービスとデータ

本リポジトリには、PLATEAU SDK、CityGML の元データ、Photon、OpenStreetMap のコードやデータを同梱していません。利用手順の画像・動画には、操作中に表示された地理院タイルと検索結果が含まれます。Scene View と Game View の画像には、港区の都市モデルを Unity で可視化した結果が含まれます。操作画面の一部画像には実画面の切り抜き、動画には待機時間のカットと出典表示の追加を行っています。自作部分の MIT ライセンスは、これらの外部サービスとデータに適用されません。

| 対象 | 本ツールでの利用 | 出典・条件 |
| --- | --- | --- |
| PLATEAU SDK for Unity 4.3.0 | 利用者が別途導入する Unity パッケージ | [公式 Release](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.3.0)、[SDK ライセンス](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/LICENSE.md)（MIT、Copyright (c) 2023 MLIT Japan） |
| PLATEAU 配信サービス・CityGML | データ検索・取得 | [配信サービスの説明](https://docs.plateauview.mlit.go.jp/intro/)、[PLATEAU サイトポリシー](https://www.mlit.go.jp/plateau/site-policy/)。都市モデルの権利者・条件・必要な出典は対象データセットごとに確認してください。 |
| 3D都市モデル（Project PLATEAU）港区（2025年度） | Scene View と Game View の掲載画像で Unity 上に可視化 | [データセット](https://www.geospatial.jp/ckan/dataset/plateau-13103-minato-ku-2025)。取得したデータセットの README に列挙されたライセンスから [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) を選択。元の CityGML は本リポジトリに含めません。 |
| 地理院タイル | 地図表示時にオンライン取得。利用手順の画面画像・動画にも映り込みます | [地理院タイル一覧・利用案内](https://maps.gsi.go.jp/development/ichiran.html)、[出典の記載](https://www.gsi.go.jp/LAW/2930-meizi.html)。画面と資料に「地理院タイル（国土地理院）」を表示します。 |
| Google Maps Platform（Map Tiles API） | 利用者が「表示する地図」で選び、自身の API キーを設定した場合だけ地図タイルをオンライン取得します。取得したタイルは表示中の Unity Editor のメモリにだけ保持します | [Google Maps Platform 利用規約](https://cloud.google.com/maps-platform/terms)、[Map Tiles API のポリシー](https://developers.google.com/maps/documentation/tile/policies)。料金と規約の遵守は API キーの所有者が負います。表示中は Google Maps ロゴと API から取得した著作権表記を地図上に表示します。 |
| Google Maps ロゴ | `Editor/GoogleMaps_Logo_WithLightOutline_2x.png`、`Editor/GoogleMaps_Logo_WithDarkOutline_2x.png` を同梱し、Google Maps 表示中の帰属表示にだけ使用 | [Google 配布の帰属表示素材](https://developers.google.com/static/maps/documentation/images/Google_Maps_Attribution_Assets.zip)を無加工で同梱。Google の商標であり、本リポジトリの MIT ライセンスは適用されません。 |
| Photon | 施設名の検索にオンライン API を利用 | [Photon API と利用条件](https://photon.komoot.io/)、[Photon のライセンス](https://github.com/komoot/photon#license)。公開サーバーの可用性は保証されず、多量の利用は制限されます。 |
| OpenStreetMap | Photon の検索データの出典 | [© OpenStreetMap contributors](https://www.openstreetmap.org/copyright)。検索結果やその画像を再利用する場合も出典を維持してください。 |

利用者が別の接続先へ変更した場合は、その提供者の利用条件を確認してください。取得した都市モデルや作成したスクリーンショットを公開するときは、使用した地域・年度・データセットの出典と条件を個別に確認してください。

過去の Git 履歴にある `Documentation~/media/01-place-search.jpg`、`02-gml-search.jpg`、`03-download-complete.jpg`、`04-sdk-local.jpg`、`05-sdk-folder-picker.jpg`、`06-sdk-folder-specified.jpg` に写る背景地図の出典は**地理院タイル（国土地理院）**です。旧画像は Unity 実画面から切り抜き、JPEG に変換したものです。特に `01-place-search.jpg` と `02-gml-search.jpg` は画像内の出典表示が欠けているため、ここに明記します。施設検索結果の出典は **Photon / © OpenStreetMap contributors** です。旧 JPEG は現行版から削除し、マニュアルには地図内出典を確認できる PNG を掲載しています。[地理院タイル一覧](https://maps.gsi.go.jp/development/ichiran.html)と[出典の記載案内](https://www.gsi.go.jp/LAW/2930-meizi.html)も参照してください。
