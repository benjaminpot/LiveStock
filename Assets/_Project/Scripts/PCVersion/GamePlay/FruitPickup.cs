using UnityEngine;

public class FruitPickup : MonoBehaviour
{
    private bool isCollected = false;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Collect()
    {
        if(isCollected) return;

        isCollected = true;

        FruitManager.Instance.CollectFruitCount();
        gameObject.SetActive(false);
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerControl player = other.GetComponentInParent<PlayerControl>();
            if(player != null)
            {
                player.EnterFruitRange(this);
                Debug.Log("玩家進入範圍，按E可吃掉頻果！");
            }
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerControl player = other.GetComponentInParent<PlayerControl>();
            if(player != null)
            {
                player.ExitFruitRange(this);
                Debug.Log("玩家離開範圍");
            }
        }
    }
}
