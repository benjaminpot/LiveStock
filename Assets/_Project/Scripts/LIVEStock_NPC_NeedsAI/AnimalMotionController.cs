using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using LiveStock.MotionRecording;

/// <summary>
/// 將 AnimalNPC 的行為狀態轉換成 VRRecordingTargetPlayer 的動作播放。
/// 此元件不負責決定去哪裡，也不負責尋路；AnimalNPC / NavMeshAgent 繼續處理這些事。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AnimalNPC))]
[RequireComponent(typeof(NavMeshAgent))]
public class AnimalMotionController : MonoBehaviour
{
    [Header("元件參考")]
    public AnimalNPC animalNPC; // 負責飢餓、口渴和目前行為狀態的 NPC 腳本。
    public NavMeshAgent agent; // 讀取 NPC 是否正在移動。
    public VRRecordingTargetPlayer motionPlayer; // 負責實際播放錄製動作的元件。

    [Header("Motion List 中的 Action Name（需與 Inspector 完全相符）")]
    public string idleMotionName = "idle"; // 原地停留時播放的動作名稱。
    public string walkMotionName = "walk"; // 移動時播放的動作名稱。
    public string runMotionName = "run"; // 快速移動時播放的動作名稱。
    public string eatMotionName = "eat"; // 進食時播放的動作名稱。
    public string drinkMotionName = "eat"; // 暫時沒有 drink recording 時先用 eat；之後可改成 drink。

    [Header("移動判斷")]
    public float movingSpeedThreshold = 0.12f; // 速度高於此值就視為正在移動。
    public bool allowRunMotion = false; // MVP 先關閉跑步切換，避免一般走路被誤判成跑步。
    public float runSpeedThreshold = 2.8f; // 開啟跑步切換後，速度達到此值才播放 run。

    private string lastRequestedMotion = ""; // 記錄上一個要求播放的動作，避免每一幀重播。
    private readonly HashSet<string> warnedMissingMotions = new HashSet<string>(); // 避免找不到動作時每一幀都刷警告。
    private bool warnedMissingPlayer; // 記錄是否已提醒缺少播放元件。

    /// <summary>
    /// 目前 Motion List 的索引。網路 NPC 只同步這個小型狀態，
    /// 不再逐 Tick 傳送整套骨架姿態。
    /// </summary>
    public int CurrentMotionIndex { get; private set; } = -1;

    private void Awake()
    {
        // 若 Inspector 沒有手動指定，就從本物件取得必要元件。
        if (animalNPC == null)
            animalNPC = GetComponent<AnimalNPC>();

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        // 播放器可能掛在同一個根物件，或羊的子物件上；先找同物件，再找子階層。
        if (motionPlayer == null)
            motionPlayer = GetComponent<VRRecordingTargetPlayer>();

        if (motionPlayer == null)
            motionPlayer = GetComponentInChildren<VRRecordingTargetPlayer>(true);
    }

    private void Update()
    {
        // 沒有動作播放器就不能切換 recording。
        if (motionPlayer == null)
        {
            if (!warnedMissingPlayer)
            {
                Debug.LogWarning($"[{name}] 找不到 VRRecordingTargetPlayer；請把播放器放在羊的根物件或子物件中。", this);
                warnedMissingPlayer = true;
            }
            return;
        }

        // VRRecordingTargetPlayer 必須使用 Motion List 才能在多個 recording 之間切換。
        if (motionPlayer.motions == null || motionPlayer.motions.Count == 0)
        {
            if (warnedMissingMotions.Add("<empty-list>"))
                Debug.LogWarning($"[{name}] VRRecordingTargetPlayer 的 Motion List 是空的，請先加入 idle / walk / eat 等動作。", motionPlayer);
            return;
        }

        // 根據 NPC 目前需求行為與 Agent 速度，選擇這一刻應播放的動作。
        string wantedMotion = ResolveWantedMotion();

        // 如果動作沒變，而且播放器仍在播放，就不重設時間，避免動作每幀從頭開始。
        if (string.Equals(wantedMotion, lastRequestedMotion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(motionPlayer.CurrentMotionName, wantedMotion, StringComparison.OrdinalIgnoreCase) &&
            motionPlayer.IsPlaying)
        {
            return;
        }

        // 嘗試播放想要的動作。若此動作不存在，就退回 idle 作為安全備援。
        if (TryPlayMotionByName(wantedMotion))
        {
            lastRequestedMotion = wantedMotion;
        }
        else if (!string.Equals(wantedMotion, idleMotionName, StringComparison.OrdinalIgnoreCase) &&
                 TryPlayMotionByName(idleMotionName))
        {
            lastRequestedMotion = idleMotionName;
        }
        else
        {
            // 沒有可播放的對應動作時，記住本次請求，避免無限制重試。
            lastRequestedMotion = wantedMotion;
        }
    }

    /// <summary>決定目前應該播放 idle、walk、run、eat 或 drink。</summary>
    private string ResolveWantedMotion()
    {
        if (animalNPC != null)
        {
            // 以下文字必須與 AnimalNPC.cs 中的 CurrentBehavior 狀態名稱相符。
            string behavior = animalNPC.CurrentBehavior ?? "";

            if (behavior == "喝水")
                return drinkMotionName;

            if (behavior == "進食" || behavior == "啄食穀物")
                return eatMotionName;
        }

        // 飲水或進食以外的行為，用 Agent 的實際速度區分移動與停留。
        float speed = agent != null ? agent.velocity.magnitude : 0f;
        if (speed > movingSpeedThreshold)
        {
            // 目前的生理需求 AI 沒有特別的奔跑／逃跑狀態，所以預設不自動切 run。
            if (allowRunMotion && speed >= runSpeedThreshold)
                return runMotionName;

            return walkMotionName;
        }

        return idleMotionName;
    }

    /// <summary>依 Motion List 裡的 actionName 找動作，而不是假設固定 index。</summary>
    private bool TryPlayMotionByName(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return false;

        for (int i = 0; i < motionPlayer.motions.Count; i++)
        {
            VRRecordingTargetPlayer.MotionEntry entry = motionPlayer.motions[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.actionName))
                continue;

            // actionName 不分大小寫比對，例如 idle 和 Idle 都可以找到。
            if (!string.Equals(entry.actionName.Trim(), requestedName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            // 已經是該動作就不需要再次呼叫 Play。
            if (string.Equals(motionPlayer.CurrentMotionName, entry.actionName, StringComparison.OrdinalIgnoreCase) &&
                motionPlayer.IsPlaying)
            {
                CurrentMotionIndex = i;
                return true;
            }

            // 使用播放元件提供的公開 API 切換到對應 recording。
            motionPlayer.Play(i);

            // 播放器成功載入時會更新 CurrentMotionName；如果載入失敗，它會清空此值並在 Console 報錯。
            if (string.Equals(motionPlayer.CurrentMotionName, entry.actionName, StringComparison.OrdinalIgnoreCase))
            {
                CurrentMotionIndex = i;
                return true;
            }

            return false;
        }

        // 找不到對應名稱時，只顯示一次警告，避免 Console 被訊息塞滿。
        if (warnedMissingMotions.Add(requestedName))
        {
            Debug.LogWarning($"[{name}] Motion List 找不到 actionName = '{requestedName}'。請確認大小寫／名稱，並指定 recording 資產。", motionPlayer);
        }

        return false;
    }
}
