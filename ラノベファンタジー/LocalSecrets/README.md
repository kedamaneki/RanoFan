# LocalSecrets

このフォルダには **Git に載せない** 秘密情報を置きます。

## Gemini API キーの設定手順

1. `gemini-api-key.txt.example` を `gemini-api-key.txt` にコピー
2. `gemini-api-key.txt` を開き、`YOUR_API_KEY` を実際のキーに置き換え（1行のみ）
3. Unity で `RealAICommunicationSystemTestRunner` を実行

`#` で始まる行はコメントとして無視されます。

## 注意

- `gemini-api-key.txt` は `.gitignore` 済みです
- キーが漏れた場合は Google AI Studio で無効化し、新しいキーを発行してください
- ゲーム本番配布時はクライアントにキーを入れず、サーバー経由に切り替えてください
