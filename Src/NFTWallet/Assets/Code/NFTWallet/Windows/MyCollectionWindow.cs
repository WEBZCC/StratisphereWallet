using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Stratis.SmartContracts;
using Unity3dApi;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class MyCollectionWindow : WindowBase
{
    public GameObject ContentGameObject;

    public Text StatusText;

    public CollectionItem CollectionCopyFromItem;

    public ScrollRect ScrollRect;

    public List<CollectionItem> SpawnedItems = new List<CollectionItem>();

    private float defaultScrollRectVerticalPosition;

    private HttpClient client = new HttpClient();

    void Awake()
    {
        defaultScrollRectVerticalPosition = ScrollRect.verticalNormalizedPosition;
    }

    public override async UniTask ShowAsync(bool hideOtherWindows = true)
    {
        this.StatusText.text = "loading collection";
        ScrollRect.verticalNormalizedPosition = this.defaultScrollRectVerticalPosition;

        // Disable prev spawned items.
        foreach (CollectionItem prevSpawn in SpawnedItems)
            prevSpawn.gameObject.SetActive(false);

        await base.ShowAsync(hideOtherWindows);

        List<DeployedNFTModel> knownNfts = NFTWallet.Instance.LoadKnownNfts();
        OwnedNFTsModel myNfts = await NFTWallet.Instance.StratisUnityManager.Client.GetOwnedNftsAsync(NFTWallet.Instance.StratisUnityManager.GetAddress().ToString());
        
        foreach (KeyValuePair<string, ICollection<long>> contrAddrToOwnedIds in myNfts.OwnedIDsByContractAddress)
        {
            string contractAddr = contrAddrToOwnedIds.Key;
            List<long> ownedIds = contrAddrToOwnedIds.Value.Distinct().ToList();

            DeployedNFTModel knownNft = knownNfts.FirstOrDefault(x => x.ContractAddress == contractAddr);
            string nftName = (knownNft == null)? string.Empty : knownNft.NftName;

            NFTWrapper wrapper = new NFTWrapper(NFTWallet.Instance.StratisUnityManager, contractAddr);
            
            // Create item for each owned ID or enable already spawned item
            for (int i = 0; i < ownedIds.Count; i++)
            {
                this.StatusText.text = contractAddr.Substring(0, 20) + "... " + (i + 1).ToString() + " / " + ownedIds.Count;
                long currentId = ownedIds[i];

                var alreadySpawnedItem = SpawnedItems.SingleOrDefault(x => x.ContractAddr == contractAddr && x.NFTID == currentId);

                if (alreadySpawnedItem != null)
                {
                    alreadySpawnedItem.gameObject.SetActive(true);
                    continue;
                }

                CollectionItem cItem = GameObject.Instantiate(CollectionCopyFromItem, ContentGameObject.transform);
                cItem.gameObject.SetActive(true);
                SpawnedItems.Add(cItem);

                cItem.NFTID = currentId;
                cItem.ContractAddr = contractAddr;
                string uri = await wrapper.TokenURIAsync((UInt256)currentId);
                cItem.NFTUri = uri;

                cItem.TitleText.text = nftName;
                cItem.DescriptionText.text = string.Format("ID: {0}", currentId);

                string sellUri = MarketplaceIntegration.Instance.GetSellURI(contractAddr, currentId);

                cItem.Sell_Button.onClick.AddListener(delegate { Application.OpenURL(sellUri); });
            }
        }

        RectTransform contentTransform = ContentGameObject.GetComponent<RectTransform>();
        GridLayoutGroup gridLayoutGroup = ContentGameObject.GetComponent<GridLayoutGroup>();
        float cellSize = gridLayoutGroup.cellSize.y;
        float spacing = gridLayoutGroup.spacing.y;
        int rows = (int)Math.Ceiling(((decimal) SpawnedItems.Count / 3));
        contentTransform.sizeDelta = new Vector2(contentTransform.sizeDelta.x, (cellSize + spacing) * rows);

        // Load images
        List<CollectionItem> notLoaded = this.SpawnedItems.Where(
            x => !x.ImageLoaded && x.NFTUri.StartsWith("https://") && 
            (x.NFTUri.EndsWith(".png") || x.NFTUri.EndsWith(".gif") || x.NFTUri.EndsWith(".jpg") || x.NFTUri.EndsWith(".json"))).ToList();

        List<UniTask<Texture2D>> loadTasks = new List<UniTask<Texture2D>>();

        for (int i = 0; i < notLoaded.Count; i++)
        {
            this.StatusText.text = "loading textures " + (i + 1).ToString() + " / " + notLoaded.Count;

            try
            {
                string uri = notLoaded[i].NFTUri;
                string imageUri;

                if (uri.EndsWith(".json"))
                {
                    string json = await this.client.GetStringAsync(uri);
                    NFTMetadataModel model = JsonConvert.DeserializeObject<NFTMetadataModel>(json);
                    imageUri = model.image;
                }
                else
                    imageUri = uri;
            
                UniTask<Texture2D> loadTask = this.GetRemoteTextureAsync(imageUri);
                loadTasks.Add(loadTask);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log(e.ToString());
            }
        }

        Texture2D[] loaded = await UniTask.WhenAll(loadTasks);

        for (int i = 0; i < loaded.Length; i++)
        {
            Texture2D texture = loaded[i];

            if (texture == null)
                continue;

            Sprite sprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100.0f);

            notLoaded[i].NFTImage.sprite = sprite;
            notLoaded[i].ImageLoaded = true;
        }

        this.StatusText.text = string.Empty;
    }

    private async UniTask<Texture2D> GetRemoteTextureAsync(string url)
    {
        Texture2D texture; 

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            var asyncOp = www.SendWebRequest();
            
            while (asyncOp.isDone == false)
                await Task.Delay(1000 / 30);//30 hertz
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.Log($"{www.error}, URL:{www.url}");
                return null;
            }
            
            texture = DownloadHandlerTexture.GetContent(www);
        }

        if (texture != null && (texture.width > 600 || texture.height > 600))
        {
            Texture2D resized = ResizeTexture(texture, 600, 600);

            Object.Destroy(texture);

            return resized;
        }

        return texture;
    }

    private Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
    {
        source.filterMode = FilterMode.Point;
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
        rt.filterMode = FilterMode.Point;
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);
        Texture2D nTex = new Texture2D(newWidth, newHeight);
        nTex.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        nTex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return nTex;
    }
}
