# PLATEAU Area Downloader

施設名や地図から範囲を選び、その範囲と交差する CityGML ファイルと参照データを取得する Unity Editor 用 UPM パッケージです。取得した都市フォルダを公式 PLATEAU SDK for Unity で選び、座標系や LOD などを指定してインポートします。

![公式PLATEAU SDKで取り込んだ港区2025年の都市モデルをUnity Game Viewに表示した状態](Documentation~/media/game-view-city-model.png)

画面例は東京タワー周辺の取得データを公式 SDK 4.3.0 で読み込み、Unity の保存・再起動後に表示したものです。実際の取得と SDK への受け渡しは[図付き操作ガイド](Documentation~/user-guide.md)と[操作動画](Documentation~/media/area-downloader-demo.mp4)で確認できます。

都市モデル画像の出典は[3D都市モデル（Project PLATEAU）港区（2025年度）](https://www.geospatial.jp/ckan/dataset/plateau-13103-minato-ku-2025)です。データセットの選択可能なライセンスから CC BY 4.0 を選び、CityGML を Unity で可視化して撮影しました。[画像と第三者データの条件](THIRD_PARTY_NOTICES.md)も参照してください。

## 1. できることと確認済みの結果

施設を検索するか地図・経緯度で範囲を決め、選択した種類の CityGML を取得できます。取得後は都市別フォルダを公式 SDK に渡します。東京タワー周辺のデータを `v0.1.0` 公開前の候補コミットから公式 SDK 4.3.0 の公開 API で再インポートし、建築物・道路・地形を Scene View と Game View で確認しました。保存したシーンは Unity 再起動後も表示できました。SDK の GUI では都市フォルダの受理まで確認し、範囲指定とインポートは API 経路で検証しています。詳しい条件と限界は[検証記録](Documentation~/validation-urp-2026-09-26.md)と[Issue #3](https://github.com/zabaglione/plateau-area-downloader/issues/3)を参照してください。

検証環境は Unity `6000.3.10f1`、Universal 3D / URP `17.3.0`、PLATEAU SDK `4.3.0` です。HDRP は導入していません。検証は macOS で行い、Windows 実機は未検証です。

`v0.1.2` では検索対象を26種類に拡張し、メニューの再実行時には既存ウィンドウを前面に出して地図を更新します。Unity Editor 上で種類一覧、橋梁の検索、ウィンドウ再利用を確認しました。[検証記録](Documentation~/validation-editor-2026-09-27.md)に条件と結果を記載しています。

## 2. 必要な環境

- Unity `6000.3.10f1` 以降。確認済み構成は Universal 3D / URP `17.3.0` です。
- Git クライアントと、CityGML・地図・施設検索サービスへ接続できる環境。
- [PLATEAU SDK for Unity `4.3.0`](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.3.0)。本パッケージより先に導入します。

本パッケージは公式 SDK のメニューを開くため SDK `4.3.0` に依存します。Unity Package Manager はパッケージの `package.json` による Git パッケージ間依存を解決できないため、SDK の `.tgz` を先に導入します。

## 3. 公式 SDK を導入する

1. [公式 Release v4.3.0](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.3.0) の Assets から `PLATEAU-SDK-for-Unity-v4.3.0.0.tgz` を取得します。ソース ZIP ではなく配布用 `.tgz` を使います。
2. Unity で **Window → Package Management → Package Manager** を開きます。
3. 左上の **＋ → Install package from tarball...** を選び、取得した `.tgz` を指定します。
4. Package Manager に `PLATEAU SDK for Unity` が表示されることを確認します。

![公式PLATEAU SDK 4.3.0をtarballから導入したPackage Manager](Documentation~/media/package-manager-sdk-install.png)

[Package Manager の文字を原寸で見る](Documentation~/media/package-manager-sdk-install.png)

### 既知の問題: SDK 4.3.0 の UXML エラー

公式 SDK `4.3.0` の配布物では、初回アセット取込時に `RoadNetworkEditor.uxml` が未定義の `PLATEAU.Editor.RoadNetwork.RoadNetworkEditMode` を参照し、`TypeLoadException` が Console に出る事例を確認しています。新規プロジェクト 2 件で各 1 回発生し、該当 UXML の強制再インポートでも再現しました。SDK 導入後、本パッケージを加える前から発生し、公式 SDK 側の問題とみられます。SDK 本体は変更していません。Unity 再起動後に Console の件数が 0 でも、初回エラーが解消したとは扱っていません。

詳細と未解決の検証条件は[Issue #2](https://github.com/zabaglione/plateau-area-downloader/issues/2)と[公式 SDK の UXML](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/Resources/PlateauUIDocument/RoadNetwork/RoadNetworkEditor.uxml#L16)を参照してください。

## 4. 本パッケージを導入する

公式 SDK の後に、Package Manager の **＋ → Install package from git URL...** を選び、次の URL を指定します。

```text
https://github.com/zabaglione/plateau-area-downloader.git#v0.1.2
```

導入後は **Tools → PLATEAU Area Downloader** から開きます。メニューを再実行すると開いているウィンドウを前面に出し、地図を再描画します。[Unity の Git URL 導入手順](https://docs.unity3d.com/ja/6000.0/Manual/upm-ui-giturl.html)も参照できます。

`v0.1.0` 公開前には別の一時プロジェクトで、公式 SDK の tarball を先に追加し、本ツールの非公開 Git URL を後から追加する二段階導入に成功しました。Package Manager は本ツールを `Git` 取得元として表示しました。[導入の検証記録](Documentation~/validation-upm-2026-09-26.md)を参照してください。

![公開前の候補SHAから本ツールをGit URLで導入したPackage Manager](Documentation~/media/package-manager-git-install.png)

[Package Manager の文字を原寸で見る](Documentation~/media/package-manager-git-install.png)

画像の導入元は `v0.1.0` 公開前の候補コミット `6774dee` です。`v0.1.2` の導入結果を示す画像ではありません。

## 5. 施設を検索する

ウィンドウの「施設名」に地名や施設名を入力して「検索」を押し、候補の住所や種別を確認して選びます。同名候補がある場合は所在地を確認してください。候補を選ぶと地図が移動し、選択中のデータ種別で検索が始まります。候補が見つからなくても地図や経緯度から範囲を指定できます。施設検索には Photon と OpenStreetMap のデータを使います。

![施設名の検索候補。住所と種別を確認して選ぶ](Documentation~/media/facility-search-results.png)

## 6. 地図で範囲を選ぶ

地図をドラッグして移動し、ホイールまたはトラックパッドの縦スクロールで拡大縮小します。「拡大縮小の速さ」は同じ端末の Unity Editor 設定に保存されます。**Shift＋ドラッグ**で青い指定範囲を描きます。経緯度を直接入力したときは「反映」を押してください。

背景地図は既定で地理院タイルです。「表示する地図」で Google Maps（道路地図・航空写真・地形）に切り替えられます。Google Maps を使うには、Google Cloud で課金を有効にしたプロジェクトの [Map Tiles API](https://developers.google.com/maps/documentation/tile) を有効化し、その API キーを「接続先」の「Google Maps API キー」に入力します。タイル取得には Google の料金がかかります。キーは同じ端末の Unity Editor 設定に平文で保存され、プロジェクトには保存されません。

地図の右側（狭いウィンドウでは下側）の「検索するデータ種別」で対象を選び、「CityGMLファイルを検索」を押します。紫色の番号付き枠は詳細区画、薄紫色の枠は広域区画です。地図に表示する区画は最大 100 件ですが、検索結果には全体の区画数とファイル数が表示されます。区画はおおむね約 1 km 単位の地域メッシュです。

![広い範囲の検索結果。15件のCityGMLファイルが見つかった状態](Documentation~/media/wide-range-results.png)

![入力範囲を反映し、検索結果が消えた状態](Documentation~/media/range-applied-before-search.png)

![範囲を絞って再検索し、5件のCityGMLファイルが見つかった状態](Documentation~/media/narrow-range-results.png)

建築物・道路・地形のほか、橋梁、鉄道、水部、災害リスクなどを含む26種類から選べます。「全選択」「選択解除」でまとめて切り替えられます。地域に公開データがない種類は検索しても結果に出ません。範囲または種類を変えたら再検索してください。ダウンロード対象は青い範囲と交差する**ファイル全体**です。CityGML が青い範囲だけに切り詰められるわけではありません。

## 7. データを確認してダウンロードする

検索結果の区画数・ファイル数を確認し、「保存先」と容量上限を確認してから「CityGMLをダウンロード」を押します。検索結果の GML 容量には参照画像などが含まれず、最終的な取得量は検索時点では不明です。

既定の保存先はプロジェクト直下の `PLATEAUData~` で、ジョブごとのフォルダにデータを保存します。画面の「保存先」で変更できますが、`Assets` 内は指定できません。既定の保存先を使う Git プロジェクトでは `/PLATEAUData~/` を `.gitignore` に追加してください。容量上限の初期値は ZIP 転送量 **10 GiB**、展開後 **30 GiB** です。値は画面で変更できます。どちらかの上限を超えると取得を失敗として止め、完了データとして表示しません。

![保存済みファイルのサイズとSHA-256を照合している進捗](Documentation~/media/saved-data-check-progress.png)

![展開済みCityGMLの参照先を確認している進捗](Documentation~/media/citygml-reference-check-progress.png)

![取得内容の検証完了と、SDKに渡す都市フォルダのパス](Documentation~/media/download-complete-sdk-path.png)

中断または失敗した場合、安全に削除できる ZIP・`.part`・`staging` は自動で整理し、再実行に使う `manifest.json` と以前に完成した `dataset` は残します。再実行では完成済みデータの整合性を確認して再利用します。未完了 ZIP は先頭から再取得し、通信途中のバイト位置からは再開しません。Unity の強制終了で途中ファイルが残ったときも、同じジョブの次回実行時に整理します。削除に失敗した場合は Console の警告と表示された保存先を確認してください。置換処理の中断で `dataset` と `dataset.previous` が両方残った場合は、安全のため自動復旧を停止します。

## 8. 都市フォルダを公式 SDK で選ぶ

取得完了後、「パスをコピーしてSDKを開く」を押します。複数都市がある場合は使う都市のボタンを選びます。公式 SDK の **インポート → 都市の追加 → ローカル → 入力フォルダ → 参照...** を開きます。

Mac のフォルダ選択画面では **⌘⇧G** を押し、コピーしたパスを貼り付けて移動します。`udx` が直下にある都市フォルダを選択してください。`dataset` 全体や `udx` 自体は選びません。

![公式SDKのローカル入力欄に都市フォルダを指定した状態](Documentation~/media/sdk-local-folder-input.png)

選択対象の構造は次のとおりです。

```text
PLATEAUData~/
└── <job-id>/
    └── dataset/
        └── <city-folder>/  ← このフォルダを選ぶ
            ├── udx/
            ├── codelists/
            └── metadata/
```

Windows でのフォルダ選択は実機未検証です。

## 9. 公式 SDK でインポートし、結果を確認する

本パッケージのボタンはインポート設定やインポート処理を行いません。公式 SDK では、インポート形式と基準座標系を選び、「範囲選択」で対象の地域メッシュを指定し、範囲選択画面の「決定」を押します。元のシーンで種類・LOD・テクスチャなどを確認して「モデルをインポート」を押します。詳しくは[公式 SDK 4.3.0 のインポート手順](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/Documentation~/manual/ImportCityModels.md)と[図付きの操作ガイド](Documentation~/user-guide.md)を参照してください。

本ガイドの動画と画像が示す SDK の GUI 操作は都市フォルダを受け付けるところまでです。`v0.1.0` 公開前の候補から取得したデータのインポート完了と再起動後の表示は、SDK の公開 API 経由で検証しました。GUI の「モデルをインポート」完了までは実操作で確認していません。

インポート後は Scene View と Game View で建築物・道路・地形を確認します。Hierarchy でモデル、Inspector で参照やマテリアルを確認し、Console に Error / Exception / Assert がないか、Missing Script、欠落マテリアル、ピンク表示がないか点検してください。`v0.1.0` 公開前の候補での保存と Unity 再起動後の結果は[検証記録](Documentation~/validation-urp-2026-09-26.md)に記載しています。

![公式SDKから取り込んだ港区の都市モデルをUnity Scene Viewで表示した状態](Documentation~/media/scene-view-city-model.png)

## 10. 困ったとき

| 状況 | 確認すること |
| --- | --- |
| SDK のメニューが出ない | 公式 SDK `.tgz` を先に導入し、Package Manager に表示されるか確認します。 |
| SDK 4.3.0 の UXML `TypeLoadException` | 上記の[既知の問題](#既知の問題-sdk-430-の-uxml-エラー)を確認します。Issue #2 に記録した未解決の公式 SDK エラーです。 |
| Git URL の導入に失敗する | Git の導入、URL、非公開段階なら GitHub へのアクセス権を確認します。SDK `.tgz` を先に導入してください。 |
| 施設検索や地図が表示されない | 接続先とネットワークを確認し、経緯度入力で範囲を指定します。 |
| 対象が多い・容量上限を超える | 範囲を狭めるか種類を減らし、「CityGMLファイルを検索」から再実行します。 |
| 中断後も保存先に一時ファイルがある | 次回同じジョブを実行してください。削除できない場合は Console の警告を確認します。 |
| SDK がフォルダを受け付けない | 直下に `udx` がある都市フォルダを選んだか確認します。 |
| インポート後に表示されない | SDK の範囲・LOD・座標系・対象種類を確認し、Scene / Game View と Console を点検します。 |

## 出典とライセンス

本パッケージの自作コードは [MIT ライセンス](LICENSE)です。公式 SDK、地理院タイル、都市モデル、Photon、OpenStreetMap は同梱せず、各提供元の条件に従います。[第三者サービスとデータ](THIRD_PARTY_NOTICES.md)を確認してください。画像の出典と撮影条件は[メディアについて](Documentation~/media/README.md)を参照してください。
