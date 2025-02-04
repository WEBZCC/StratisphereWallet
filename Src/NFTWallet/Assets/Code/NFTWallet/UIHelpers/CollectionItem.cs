using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class CollectionItem : MonoBehaviour
{
    public Text TitleText, DescriptionText;

    public Image NFTImage;

    public Button Sell_Button;

    public Button DisplayAnimationButton;

    [HideInInspector]
    public long NFTID;

    [HideInInspector]
    public string ContractAddr;

    [HideInInspector]
    public string NFTUri;

    [HideInInspector] 
    public bool ImageLoaded = false;
}
