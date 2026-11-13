using UnityEngine;
using UnityEngine.UI;
public class FruitManager : MonoBehaviour
{
    public static FruitManager Instance;
    [SerializeField] private int requiredFruitCount = 3;
    [SerializeField] private Image[] appleIcons;
    private int collectedFruitCount = 0;

    private void Awake()
    {
        Instance = this;
    }
    private void Start(){
        UpdateAppleUI();
    }
    public void CollectFruitCount() //負責計算剩幾顆蘋果，呼叫改變場景
    {
        collectedFruitCount++;
        Debug.Log($"目前吃掉 {collectedFruitCount} / {requiredFruitCount} 顆蘋果");
        UpdateAppleUI();
        if (collectedFruitCount >= requiredFruitCount)
        {
            Debug.Log("任務完成！出口開啟！");
        }
    }
    private void UpdateAppleUI()
    {
        // 更新 UI 顯示已收集的蘋果數量
        // 這裡可以使用 Unity 的 UI 系統來顯示數量，例如 Text 或 Image 元件
        for(int i=0;i<appleIcons.Length;i++)
        {
            appleIcons[i].gameObject.SetActive(i>=collectedFruitCount);
        }
    }

    
}
