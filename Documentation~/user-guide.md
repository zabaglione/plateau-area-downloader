# 使い方：場所を選び、CityGMLを公式SDKへ渡す

このガイドでは、PLATEAU Area Downloader で取得した都市フォルダを公式 PLATEAU SDK for Unity に渡し、インポート後の表示を確認するまでを10段階で説明します。導入手順と検証条件は[README](../README.md)も参照してください。

画面例は macOS、Unity `6000.3.10f1`、PLATEAU SDK `4.3.0` で東京タワー周辺を操作したものです。検索結果の区画数・ファイル数・容量は場所や公開データによって変わります。Windows 実機は未検証です。

## 操作動画

[実際の画面操作を見る（MP4）](media/area-downloader-demo.mp4)

動画には施設検索、データ種別の選択、経緯度の手入力、検索、保存済みデータの照合、公式 SDK の都市フォルダ指定が含まれます。待ち時間を短くするため場面の間をカットしています。最初の広い範囲では CityGML 15 ファイルが見つかり、その後で範囲を縮めて再検索した結果は 5 ファイルです。ダウンロード場面は取得済みデータをサイズと SHA-256 で再照合したもので、新規ネットワーク転送の記録ではありません。音声はありません。

## 1. できることと確認済みの結果

施設または地図で範囲を決め、そこに交差する CityGML ファイルと参照データを取得します。取得後は都市別フォルダを公式 SDK で選び、SDK 側でインポート条件を指定します。東京タワー周辺の検証では建築物・道路・地形が表示されました。検証条件と結果は[記録](validation-urp-2026-09-26.md)にあります。

## 2. 必要な環境

Unity `6000.3.10f1` 以降を使います。確認済みの構成は Universal 3D / URP `17.3.0` と公式 SDK `4.3.0` です。HDRP は使っていません。Git クライアントとネットワーク接続も必要です。

## 3. 公式 SDK を先に導入する

[公式 Release v4.3.0](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.3.0) から `PLATEAU-SDK-for-Unity-v4.3.0.0.tgz` を取得します。Unity の **Window → Package Management → Package Manager** を開き、左上の **＋ → Install package from tarball...** で `.tgz` を指定します。Package Manager に公式 SDK が表示されたことを確認します。

![公式PLATEAU SDK 4.3.0をtarballから導入したPackage Manager](media/package-manager-sdk-install.png)

[Package Manager の文字を原寸で見る](media/package-manager-sdk-install.png)

本パッケージは公式 SDK に依存し、SDK のインポート処理を複製していません。Unity Package Manager がパッケージの `package.json` による Git パッケージ間依存を解決できないため、SDK を先に導入してください。

## 4. 本パッケージを導入する

公式 SDK の後、Package Manager の **＋ → Install package from git URL...** に次の URL を入力します。

```text
https://github.com/zabaglione/plateau-area-downloader.git#v0.1.2
```

導入後は **Tools → PLATEAU Area Downloader** を開きます。同じメニューを再実行すると、開いているウィンドウを前面に出して地図を更新します。選択中の種類と検索結果は保持されます。

公式 SDK の tarball と本ツールの非公開 Git URLを別の一時プロジェクトに順番に追加した結果は[Package Manager検証記録](validation-upm-2026-09-26.md)にあります。

![公開前の候補SHAから本ツールをGit URLで導入したPackage Manager](media/package-manager-git-install.png)

[Package Manager の文字を原寸で見る](media/package-manager-git-install.png)

画像の導入元は `v0.1.0` 公開前の候補コミット `6774dee` です。`v0.1.2` の導入結果を示す画像ではありません。

## 5. 施設を検索する

ウィンドウの「施設名」に地名や施設名を入力して「検索」を押します。候補の住所や種別を確かめて、目的の施設を選びます。同じ名前の施設が複数ある場合は所在地を確認してください。施設候補を使わずに地図や経緯度から始めてもかまいません。

施設検索の出典は Photon / © OpenStreetMap contributors、背景地図は地理院タイル（国土地理院）です。

![施設名の検索候補。住所と種別を確認して選ぶ](media/facility-search-results.png)

## 6. 範囲とデータ種別を選ぶ

地図をドラッグすると移動し、ホイールまたはトラックパッドの縦スクロールで拡大縮小します。「拡大縮小の速さ」は端末に合わせて調整できます。**Shift＋ドラッグ**で青い指定範囲を描きます。経緯度を直接編集した場合は「反映」を押してください。

背景地図は「表示する地図」で地理院タイルと Google Maps を切り替えられます。Google Maps では道路地図・航空写真・地形を選べます。利用には、Google Cloud で課金を有効にしたプロジェクトの [Map Tiles API](https://developers.google.com/maps/documentation/tile) の有効化と API キーが必要です。キーはウィンドウ下部「接続先」の「Google Maps API キー」に入力し、Enter で確定します。タイル取得には Google の料金がかかります。キーは同じ端末の Unity Editor 設定に平文で保存され、プロジェクトには保存されません。Google Maps 表示中は地図左下に Google Maps ロゴ、右下に Google から取得した著作権表記を表示します。

地図の右側（狭いウィンドウでは下側）で「検索するデータ種別」から対象を選び、「CityGMLファイルを検索」を押します。種類一覧はスクロールできます。建築物・道路・地形に加え、橋梁、トンネル、鉄道、水部、災害リスクなど計26種類を選べます。「全選択」でまとめて選び、「選択解除」で全て外せます。地域に公開データがない種類は結果に出ません。全選択時は取得量が大きくなることがあるため、結果のファイル数と容量を確認してください。範囲または種類を変えた後は再検索してください。地域メッシュはおおむね約 1 km 単位です。紫色の番号付き枠は検索で見つかった詳細区画、薄紫色の枠は広域区画です。地図上の区画表示は最大 100 件で、件数欄には検索結果全体が表示されます。

![広い範囲の検索結果。15件のCityGMLファイルが見つかった状態](media/wide-range-results.png)

![入力範囲を反映し、検索結果が消えた状態](media/range-applied-before-search.png)

![範囲を絞って再検索し、5件のCityGMLファイルが見つかった状態](media/narrow-range-results.png)

青い範囲と交差した**ファイル全体**がダウンロードされます。CityGML が青い枠で切り抜かれるわけではありません。検索件数は場所や公開データによって変わります。背景地図は地理院タイル（国土地理院）です。

## 7. データを確認してダウンロードする

検索結果のファイル数と容量を確認し、「保存先」と「ダウンロード上限 GiB」「展開上限 GiB」を見てから「CityGMLをダウンロード」を押します。初期上限は ZIP 転送量 10 GiB、展開後 30 GiB です。上限は画面で変更できます。超過時は処理を失敗として止め、完了したデータとして表示しません。検索結果の GML 容量は参照画像などを含まないため、最終的な取得量とは異なります。

既定の保存先はプロジェクト直下の `PLATEAUData~` です。ジョブ別フォルダに保存し、「保存先」で変更できます。`Assets` 内は指定できません。既定の保存先を使う Git プロジェクトでは `/PLATEAUData~/` を `.gitignore` に追加してください。

進捗欄には ZIP 取得、展開・ファイル記録、展開済みファイルの参照先確認、保存済みファイルのサイズと SHA-256 の照合、記録確定などが表示されます。転送・展開の後の確認も終わるまで待ってください。

![保存済みファイルのサイズとSHA-256を照合している進捗](media/saved-data-check-progress.png)

![展開済みCityGMLの参照先を確認している進捗](media/citygml-reference-check-progress.png)

![取得内容の検証完了と、SDKに渡す都市フォルダのパス](media/download-complete-sdk-path.png)

完了画面は、範囲を縮めて再検索した後、港区の 5 CityGML ファイルと関連データを含む 6,907 ファイルを確認した例です。表示数値と保存先は撮影時のものです。背景地図は地理院タイル（国土地理院）です。

中断・失敗時は安全に削除できる ZIP・`.part`・`staging` を自動で整理し、再実行に使う `manifest.json` と以前の完成済み `dataset` を残します。再実行時に完成済みデータは整合性を確認して再利用します。未完了 ZIP は先頭から再取得され、通信途中からの再開はしません。強制終了後の残りファイルも同じジョブの次回実行時に整理します。削除できなかった場合は Console の警告と保存先を確認してください。`dataset` と `dataset.previous` が両方残った場合は自動復旧を停止し、誤削除を避けます。

## 8. 都市フォルダを公式 SDK に渡す

取得完了後、都市名の下にある「パスをコピーしてSDKを開く」を押します。複数都市がある場合は使う都市を選びます。公式 SDK の **インポート → 都市の追加 → ローカル → 入力フォルダ → 参照...** を開きます。

Mac のフォルダ選択画面で **⌘⇧G** を押し、コピーしたパスを貼り付けて Return を押します。直下に `udx` がある都市フォルダを選び、**Choose** を押します。Windows は選択画面のアドレス欄へパスを入力する方法がありますが、Windows 実機での操作は未検証です。

![公式SDKのローカル入力欄に都市フォルダを指定した状態](media/sdk-local-folder-input.png)

選択する場所は `<city-folder>` です。`dataset` 全体や `udx` 自体を選ばないでください。

```text
PLATEAUData~/
└── <job-id>/
    └── dataset/
        └── <city-folder>/  ← このフォルダを選ぶ
            ├── udx/
            ├── codelists/
            └── metadata/
```

## 9. 公式 SDK でインポートし、表示を確認する

「パスをコピーしてSDKを開く」ボタンはパスの受け渡しと SDK の起動を行い、インポートは開始しません。SDK が都市フォルダを受け付けたら、[公式 SDK 4.3.0 のインポート手順](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/Documentation~/manual/ImportCityModels.md)に沿って操作します。

1. インポート形式の「シーンに配置」などと、対象地域の基準座標系を選びます。
2. 「範囲選択」を押し、地図上で取り込む地域メッシュをクリックまたはドラッグで選びます。シーンに未保存の変更があれば、表示される保存確認を処理します。
3. 範囲選択画面の「決定」を押して元のシーンへ戻ります。
4. 地物別設定で種類・LOD・テクスチャなどを確認し、「モデルをインポート」を押します。処理の進捗は SDK ウィンドウを下へスクロールして確認できます。

このガイドの操作動画と画像が示す SDK の GUI 操作は、都市フォルダを受け付けるところまでです。別の検証プロジェクトで本ツールから取得済みの都市フォルダを使い、新規 URP プロジェクトで SDK の公開 API によるインポートと再起動後の表示を検証しました。その後、新規 URP プロジェクトで改めて取得したフォルダのファイルサイズと SHA-256 が、インポートに使ったフォルダの manifest と一致することを確認しました。GUI の「モデルをインポート」完了までは実操作で確認していません。結果と条件は[検証記録](validation-upm-2026-09-26.md)に記載しています。

処理後、Scene View と Game View で表示を確認します。Hierarchy で都市モデル、Inspector で参照やマテリアルを見て、Console の Error / Exception / Assert、Missing Script、欠落マテリアル、ピンク表示を点検します。シーンを保存して Unity を再起動した後に再確認すると、保存した結果も確かめられます。

![保存した都市モデルをUnity Scene Viewで確認した状態](media/scene-view-city-model.png)

![同じ都市モデルをUnity Game Viewで確認した状態](media/game-view-city-model.png)

両画像の出典は[3D都市モデル（Project PLATEAU）港区（2025年度）](https://www.geospatial.jp/ckan/dataset/plateau-13103-minato-ku-2025)です。データセットの選択可能なライセンスから CC BY 4.0 を選び、CityGML を Unity で可視化して撮影しました。元データは本パッケージに含めていません。

## 10. 困ったときと既知の問題

公式 SDK `4.3.0` では、`RoadNetworkEditor.uxml` が未定義の `PLATEAU.Editor.RoadNetwork.RoadNetworkEditMode` を参照し、`TypeLoadException` が発生する問題を確認しています。初回アセット取込を行った新規プロジェクト 2 件で各 1 回、該当 UXML の強制再インポートでも再現しました。本パッケージを導入する前から起きた公式 SDK 側の問題とみられ、SDK 本体は変更していません。再起動後の Console が 0 件でも、エラー解消とは扱っていません。詳細は[Issue #2](https://github.com/zabaglione/plateau-area-downloader/issues/2)と[公式 UXML](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/Resources/PlateauUIDocument/RoadNetwork/RoadNetworkEditor.uxml#L16)にあります。

- SDK のメニューが出ない：公式 `.tgz` が Package Manager に表示されることを確認します。
- Git URL の導入に失敗する：SDK を先に導入し、Git、URL、非公開段階なら GitHub へのアクセス権を確認します。
- 検索結果が出ない：ネットワークと接続先を確認し、地図または経緯度から範囲を指定します。
- ファイル数・容量が多い：青い範囲を狭め、種類と上限を見直して再検索します。
- 中断後に進まない：同じジョブを再実行し、Console の削除警告や保存先の空き容量を確認します。
- SDK がフォルダを受け付けない：選択フォルダの直下に `udx` があるか確かめます。
- インポート後に表示されない：SDK の範囲、LOD、座標系、種類、Scene / Game View、Console を確認します。

画像と動画は2026-09-26に macOS 上の Unity 実画面から取得・編集しました。地図画像は地理院タイル（国土地理院）、施設検索結果は Photon / © OpenStreetMap contributors です。詳しい出典と利用条件は[メディアについて](media/README.md)と[第三者サービスとデータ](../THIRD_PARTY_NOTICES.md)を参照してください。
