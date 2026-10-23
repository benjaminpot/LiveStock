using Fusion;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    /// <summary>讓任何玩家都能點羊，但只有伺服器能修改同步狀態。</summary>
    public class NetworkSheepSelectable : NetworkBehaviour
    {
        // Networked 會保存狀態，所以晚加入的玩家也會看到正確顏色。
        [Networked, OnChangedRender(nameof(ApplySelectedLook))]
        public NetworkBool IsSelected { get; private set; }

        Renderer[] sheepRenderers;
        MaterialPropertyBlock colorBlock;

        public override void Spawned()
        {
            // Unity 物件要在生命週期內建立，不能放在 MonoBehaviour 欄位初始化器。
            colorBlock = new MaterialPropertyBlock();
            sheepRenderers = GetComponentsInChildren<Renderer>(true);
            ApplySelectedLook(); // OnChangedRender 不會在第一次生成時自動執行。
        }

        /// <summary>由本機觀察者呼叫，向伺服器要求切換狀態。</summary>
        public void RequestToggle()
        {
            if (Object != null && Object.IsValid)
                RPC_ToggleSelected();
        }

        // 所有玩家都可提出要求，但這段只會在 State Authority（伺服器）執行。
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ToggleSelected()
        {
            if (Object.HasStateAuthority)
                IsSelected = !IsSelected;
        }

        // 這裡只處理外觀，不改遊戲規則或網路狀態。
        void ApplySelectedLook()
        {
            colorBlock ??= new MaterialPropertyBlock();
            if (sheepRenderers == null)
                sheepRenderers = GetComponentsInChildren<Renderer>(true);

            foreach (var sheepRenderer in sheepRenderers)
            {
                colorBlock.Clear();
                if (IsSelected)
                {
                    colorBlock.SetColor("_BaseColor", Color.yellow); // URP
                    colorBlock.SetColor("_Color", Color.yellow);     // Standard Shader
                }
                sheepRenderer.SetPropertyBlock(colorBlock);
            }
        }
    }
}
