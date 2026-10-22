# LIVEStock NPC 生理需求 AI（Unity 6）

這一版把原本的任務型行為改為「飢餓／口渴需求 + 閒晃」：不再有 Task Progress、任務完成條件、狗巡邏順序或馬跑訪任務。兩個 C# 檔案都附有繁體中文註解。

## 檔案
- `AnimalNPC.cs`：需求值、優先順序、加權隨機選點、避免近期重複、NavMesh 路徑可達性檢查、Route 中途點。
- `AnimalWaypoint.cs`：地圖目標點的類型、區域、可用開關、抽選權重與 Scene Gizmo。

## 安裝
1. 備份舊的兩個 Script，並將這兩個新檔案放進 `Assets/_Project/Scripts/NPC/`。
2. 等 Unity 編譯完成，確認 Console 沒有紅色錯誤。
3. 場景需有已 Bake 的 NavMesh；動物起始位置和所有目標點都需位於可走區域。
4. 動物 Prefab 需有 `NavMeshAgent` 與 `AnimalNPC`。在 Inspector 設定 `Animal Type`、`Home Zone`、需求速度與門檻。
5. `Waypoints` 欄位可留空，腳本會自動搜尋場景內的 `AnimalWaypoint`。場景很大時也可以手動指定清單。

## 建議 Waypoint 類型
- 草原區：放數個 `Wander` 點，設定 `Zone = Grassland`；另放數個 `Grass` 給馬、羊、牛覓食。
- 森林區：放數個 `Wander` 點，設定 `Zone = Forest`；如果狗平常住在森林，將狗的 `Home Zone` 設為 `Forest`。
- 農場區：放數個 `Wander` 點，設定 `Zone = Farm`；雞使用 `ChickenFeed`，狗使用 `Food`。
- 河流／池塘沿岸：放多個 `Water` 點，分散在不同岸邊與可接近的位置。點必須在 NavMesh 上、動物能站立的地方，而非深水中央。
- 全地圖：在路口、穀倉旁、森林邊緣、橋前後等位置放數個 `Route` 點。NPC 前往資源或閒晃目的地時，有機會隨機選一個作中途點。
- 每個點的 `Selection Weight` 越大，在其他條件相近時越容易被選到；先用預設值 1 即可。

## 行為優先順序
1. `Thirst >= Thirsty Threshold`：優先找可達水源。若找不到水源、同時也很餓，會退而找食物；若仍沒有目標就停下來並稍後重試。
2. 否則 `Hunger >= Hungry Threshold`：找符合食性的食物。
3. 否則：從 `Home Zone` 內的可達閒晃點隨機選擇；如果家區沒有可達點，才擴大搜尋其他區域。
4. 候選目標只會包含可用且有完整 NavMesh 路徑的點。
5. 若有未在最近造訪記憶中的候選，會優先避開最近造訪點；如果別無選擇，才允許重複。
6. 選點會根據 `Selection Weight` 和路徑距離做加權隨機。設定 `Intermediate Route Chance`，可控制途中改經 `Route` 點的機率；路徑繞太遠或不能完整到達終點時會排除。

## 動物食物類型
- Chicken：`ChickenFeed`
- Horse：`Grass`
- Sheep：`Grass` 或舊版的 `SheepGrass`
- Dog：`Food`
- Cow：`Grass`
- 所有動物的口渴需求都找 `Water`。

## 不同路徑的地圖設定重點
- 如果想讓同一水源有不同接近方向，請在水源不同岸邊放多個 `Water` waypoint，而不是只放一個點。
- 如果想讓同一終點有不同中途路線，請在真實可行走的分岔路、穀倉側邊、林緣、橋的兩端等位置放 `Route` waypoint。
- 若地圖只有唯一一座橋，NPC 仍會共用這個瓶頸；隨機中途點不會憑空創造另一條路。
- `Route` waypoint 的起點到中途點、以及中途點到終點都必須有完整 NavMesh 路徑；過遠的繞路會依 `Max Route Detour Multiplier` 被排除。

## 建議先測試的 Inspector 值
- `Hunger` / `Thirst`：20
- `Hungry Threshold` / `Thirsty Threshold`：60
- `Recent Waypoint Memory`：4
- `Intermediate Route Chance`：0.65
- `Max Route Detour Multiplier`：1.8
- `Minimum Route Leg Distance`：2
- `Distance Penalty`：0.025

## 與舊版相容性
`AnimalWaypointType` 保留舊版 `HorseRun`、`SheepGrass`、`DogPatrol` 的 enum 順序，避免舊場景序列化的 enum 數值被重新對應。新版不再計算任務進度：`HorseRun` 僅作為馬的閒晃點、`DogPatrol` 僅作為狗的閒晃點、`SheepGrass` 作為羊可吃的草地點。

## 限制
- 這是可繼續開發的 MVP 程式碼；目前未在使用者的 Unity 專案裡實際執行，因此請先在複製的測試場景驗證。
- 不含動畫、真正消耗式食物／水源、玩家威脅反應、群聚行為與 Photon 網路同步。
- `Route` 點增加路線多樣性，但不保證每隻動物一定走完全不同的路；NavMesh 仍會根據可行走區域與路徑成本選擇路線。
