# 0.2.0の検証記録

2026-10-01、macOS、Unity 6000.3.10f1、PLATEAU SDK 4.3.0。PR #9を通信障害・UI・必要時初期化の修正込みでmainへ統合したコミットは `8fe6c8557f3a674e92ff7fb004e5941b7afb140a`。その後、日付変更線の根本対応を実装した。Windows実機は未検証。

## 日付変更線の対応

中心経度を[-180,180)へ正規化する。範囲選択中の端点は同じ世界コピーの未正規化経度を保持し、完成した範囲を正規化する。`GeoBounds`ではWest > Eastを日付変更線を跨ぐ範囲として扱い、LongitudeSpan・CenterLongitude・Contains・Intersectsも対応する。これは以前の逆経度を拒否する仕様からの変更である。

地図上では範囲の経度幅を使って描画し、跨ぐ細い範囲が地球を一周する大きな矩形にならないようにした。画面に入る世界コピーへ描画する。範囲入力の中心、ドラッグ移動、Shift＋ドラッグ、ホイールズームにも対応する。

Googleのviewportは正規化した西・東経度を使用する。west=170 / east=-170の跨ぎ表現は[Googleの公式仕様](https://developers.google.com/maps/documentation/tile/2d-tiles-overview#viewport_information_requests)に従う。PLATEAUのCityGML照会は通常順の二つの矩形へ分割し、±180度ちょうどの端で零幅の余分な照会を出さない。両方の照会で同じGMLが返る場合はURLで重複排除し、容量を二重計上しない。PLATEAUの提供地域を拡張する変更ではない。

## 最終EditModeテスト

| 環境 | 全件 | 失敗・スキップ |
|---|---:|---:|
| URP 17.3.0 | 89/89成功 | 0 |
| HDRP 17.3.0 | 89/89成功 | 0 |
| Built-in（Burst無効） | 89/89成功 | 0 |

89件は、PR #9の受け入れ修正を含む既存60件と日付変更線の新規29件。正負180度、181/-181、200、複数世界分の座標変換、幅360度以上、1km範囲の境界、包含・交差、地域メッシュ、API照会の分割と通常範囲の1回照会、GML重複排除を検査した。

実EditorWindowでは座標入力、細いoverlay、Shift＋ドラッグ、日付変更線でのpanとwheel、正規化viewportと帰属情報によるタイル表示をUI Toolkitイベント送出で検査した。これらのGoogleセッション・画像・帰属情報は模擬。複数世界分の移動は座標計算テストであり、複数周の物理ドラッグを示すものではない。

初回の84/86結果では操作イベントのtarget未指定により2件が失敗した。target指定後の対象2件は2/2成功、その後に全件を再実行した。新しいテスト補助コードにあった非公開Clickable APIの使用は、公開NavigationSubmitEventによる送出へ修正した。最終結果と途中の失敗を区別する。

Built-inの初回バッチはBurstコンパイラ内のネイティブクラッシュ（終了コード138）で結果XMLを保存できなかった。[公式実行時オプション](https://docs.unity3d.com/ja/Packages/com.unity.burst@1.8/manual/getting-started.html) `--burst-disable-compilation` で再実行し、89/89成功・終了コード0を確認した。製品やSDKのソース・設定は変更しない。

0.2.0候補で港区の通常範囲（西139.744・南35.658・東139.746・北35.660、bldg/tran/dem）を再び実API照会した。5 GMLで、以前の取得manifestと対象URL＋metadataに基づくkey・GML容量が一致した。取得対象の回帰確認であり、ダウンロード・SDKインポートの再実行ではない。

URPの通常Editorを再起動し、日付変更線付近（西179.999・東-179.999、南-16.831・北-16.829）を座標入力経路で反映した。細い矩形の表示を目視し、実マウスドラッグで30px移動しても選択範囲が崩れないことを確認した。最初はキー未設定の案内だったが、利用者が設定後に再起動し、実Googleセッション・タイル・viewport帰属情報の取得に成功した。実タイル、Googleロゴ、「地図データ ©2026」、細い選択矩形を実画面で確認した。copyrightPresent=true・tilesVisible=true・messagePresent=falseを値だけで記録した。さらに実マウスで80px移動し、取得中の一時的なタイル隠蔽後に、実タイルと著作権表記が復旧することを目視した。模擬APIの結果とは別の証拠である。キー値は読み出し・記録・変更せず、利用者自身の入力に任せた。一時helperと設定画面を開くための補助コードは撤去した。provider設定は開始前に戻し、利用者が設定したキーは保持した。


## PR #9受け入れ時に実施した別の検証

P1/P2修正を適用した段階では、実ループバックHTTPによる本文停止の約15秒タイムアウト、応答送信後の取消、HTTP 503、copyright 503→200による自動復旧を確認した。TLS経路や外部Google障害の再現ではない。必要時初期化後にも模擬Stream・非同期取消・古いセッション完了の回帰テストを実施した。

港区2025年度の建築物・道路・地形を実APIから新規取得した。5 GML、ZIP 90,868,025 bytes、展開後775,850,188 bytes、6,907ファイル。SDK公開APIで同じデータをBuilt-in / URP / HDRPへインポートし、各11,713 MeshRenderer、15,085テクスチャ付きマテリアル参照、Missing Script・Missing Material・InternalErrorShader各0を確認した。Scene/Game Viewの表示とHierarchy/Inspectorを目視確認し、URP/HDRPでは保存・再起動後も一致した。SDK GUIの最終インポートボタンを通した連続操作ではない。日付変更線修正後のCityGML再取得・SDK再インポートを示す結果ではない。

560pxと759/760/761px幅のGoogle/地理院表示は、前段のUIパッチで可視操作部品が領域内・横スクロール0を確認した。Googleのキー欄とロゴは必要時だけ初期化し、地理院だけの新規起動ではGoogle要求0件・キー設定不要を検査した。コードとロゴの同梱・Editorアセンブリのコンパイルは残る。Google SDKは追加していない。

## 残る制約

- Windows実機、外部Google障害・TLS障害の再現は未検証。
- 利用者が先に導入する公式SDK 4.3.0の初回UXML TypeLoadExceptionは[Issue #2](https://github.com/zabaglione/plateau-area-downloader/issues/2)を参照。
- Google実キーの値は検証記録・ログへ出力せず変更しない。模擬キーは保存通知なしでテストウィンドウへ設定する。
