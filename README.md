# LiveStock Unity 專案說明
總之，讀一下沒壞處　：）

## 索引

1. [專案基本規則](#1-專案基本規則)
2. [每日開始工作流程](#2-每日開始工作流程)
3. [新增功能流程](#3-新增功能流程)
4. [新增素材流程](#4-新增素材流程)
5. [Google Drive 大型素材管理](#5-google-drive-大型素材管理）)
6. [提交與上傳流程](#6-提交與上傳流程)
7. [Pull Request 與合併](#7-pull-request-與合併)
8. [常用 Git 指令](#8-常用-Git-指令)

---

# 每日快速流程




```bash
#開始工作，拉最新版本
git switch main
git pull

#我要寫繼續寫xxx功能了
git switch -c feature/xxx

#開 Unity 開發
ch main

#寫寫寫...寫一個段落...該存檔了
git status
git add .
git commit -m "I FINALLY FIX THE FXXKING BUG"
git push -u origin feature/xxx #第一次，之後直接 git push 就可以了
 

# GitHub Pull Request
# Review / Merge
git switch main
git pull

#收工
```


---



## 1. 專案基本規則

- `main` 只放穩定版本，不直接在 `main` 開發。 （希望啦，可能嫌麻煩就直接覆蓋過去了呵呵）

- 一個功能使用一個 branch。
- 不要同時修改同一個 Scene 或 Prefab。

- 大型素材丟 Google Drive ，要不然github額度會爆
- 檔案移動Prefab、Scene、Material 等，盡量在 Unity Project 視窗中移動，不要直接用檔案總管/vscode搬動。

建議 branch 命名：

```text
feature/功能名稱
fix/問題名稱
refactor/整理名稱
```

例如：

```text
feature/vr-player
feature/network-lobby
fix/player-jump
```

---

## 2. 每日開始工作流程 （非常重要ಠ_ಠ）

### Step 1：先關閉 Unity

建議先關閉 Unity，再進行 Pull 或切換 Branch。

### Step 2：切到 main

```bash
git switch main
```

### Step 3：更新專案

```bash
git pull
```

### Step 4：確認素材版本（待處理）

查看專案中的：

```text
ASSET_VERSION.md
```

確認自己的 `Assets/_ExternalAssets/` 是否為相同版本。

如果版本不同，先到 Google Drive 下載最新版素材包，再開 Unity。

---

## 3. 新增功能流程

假設今天要做玩家移動：

```bash
git switch main
git pull
git switch -c feature/player-movement
```



---

## 4. 新增素材流程

### 小型素材

小型圖片、Material、Prefab 等可直接放進 Git 管理的資料夾，例如：

```text
Assets/_Project/
```

Unity 產生的 `.meta` 一定要保留並一起 commit。

### 大型素材

大型 3D 模型、貼圖、音效、影片等放在：

```text
Assets/_ExternalAssets/
```

這個資料夾由 Google Drive 管理，不直接上 GitHub。

常見內容：

```text
FBX
大型 Texture
WAV
Animation
Environment Assets
```

---

## 5. Google Drive 大型素材管理
###（這部分之後再說吧，我再研究研究 :P）

建議資料夾結構：

```text
LiveStock/
├── UnityAssets/
│   ├── Current/
│   └── Releases/
│       ├── Assets_v0.1.0.zip
│       ├── Assets_v0.1.1.zip
│       └── Assets_v0.2.0.zip
├── SourceAssets/
│   ├── Blender/
│   ├── PSD/
│   └── RawAudio/
└── CHANGELOG.md
```

### 更新素材時

例如：

```text
v0.2.0 → v0.2.1
```

請同時更新：

1. Google Drive 的素材包
2. `CHANGELOG.md`
3. GitHub 中的 `ASSET_VERSION.md`

範例：

```markdown
Required Asset Version: v0.2.1
```

重要：大型素材的 `.meta` 也要一起保留，避免 Unity GUID 不一致。

---

## 6. 提交與上傳流程

完成工作後：

### 查看修改

```bash
git status
```

### 加入修改

```bash
git add .
```

### 建立 Commit

```bash
git commit -m "feat: add player movement"
```

常用格式：

```text
feat: 新增功能
fix: 修正問題
refactor: 整理程式
chore: 專案設定或雜項
docs: 文件修改
```

### Push

第一次 push 新 branch：

```bash
git push -u origin feature/player-movement
```

之後同一個 branch：

```bash
git push
```

---

## 7. Pull Request 與合併

Push 完後到 GitHub：

```text
Pull Request
→ Review
→ Merge into main
```

Pull Request 內容簡單寫：

```text
完成內容：
- 新增玩家移動
- 新增跳躍

測試：
- PlayerTest Scene 正常
- Console 無 Error
```

Merge 完之後，本機更新：

```bash
git switch main
git pull
```

如果功能 branch 已完成，可刪除：

```bash
git branch -d feature/player-movement
```

---






## 8. 常用 Git 指令
###（不想打的話，VS code有按鈕可以直接按）

查看狀態：

```bash
git status
```

查看目前 branch：

```bash
git branch
```

切換 branch：

```bash
git switch branch名稱
```

建立新 branch：

```bash
git switch -c feature/功能名稱
```

更新 main：

```bash
git switch main
git pull
```

提交：

```bash
git add .
git commit -m "feat: ..."
```

上傳：

```bash
git push
```

查看最近 commit：

```bash
git log --oneline
```



