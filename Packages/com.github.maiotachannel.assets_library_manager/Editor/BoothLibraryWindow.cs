using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using static AssetLibraryManager;

public class AssetLibraryManager : EditorWindow
{

    private string url = "https://accounts.booth.pm/library";
    public List<Item> items = new List<Item>();
    private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
    private HashSet<string> downloadedFiles = new HashSet<string>();
    private Vector2 scrollPosition;
    private string cookies = "";
    private string workDirectory = Application.dataPath;
    private int selectedTab = 0;
    private string[] tabs = { "Content", "Settings" };
    private string jsonFilePath;
    public List<string> tagOptions = new List<string> { "�A�o�^�[", "�ߑ�", "����", "�}�e���A��", "�e�N�X�`��", "�M�~�b�N", "���̑�" };
    private string searchQuery = "";
    private int selectedTagMask = -1; // ���ׂẴ^�O��I��
    private string urlMappingsFilePath = "Assets/urlMappings.json"; // URL�}�b�s���O�t�@�C���̃p�X
    private List<UrlMapping> urlMappings = new List<UrlMapping>();
    private const int ItemsPerPage = 20; // 1�y�[�W������̃A�C�e����
    private int currentPage = 0; // ���݂̃y�[�W
    private bool deleteZipAfterUnpacking = false; // �𓀌��Zip�t�@�C�����폜���邩�ǂ���
    private string libraryDirectory;


    [MenuItem("Window/Asset Library Manager")]
    public static void ShowWindow()
    {
        GetWindow<AssetLibraryManager>("Asset Library Manager");
    }

    private void OnEnable()
    {
        LoadSettings();
        libraryDirectory = Path.Combine(workDirectory, "BoothLibrary");
        if (!Directory.Exists(libraryDirectory))
        {
            Directory.CreateDirectory(libraryDirectory);
        }
        jsonFilePath = Path.Combine(libraryDirectory, "libraryData.json");

        if (File.Exists(jsonFilePath))
        {
            _ = LoadDataFromJson();
        }
        else
        {
            _ = FetchData();
        }

        LoadUrlMappings();
    }

    private void OnGUI()
    {
        selectedTab = GUILayout.Toolbar(selectedTab, tabs, GUILayout.Height(30));

        switch (selectedTab)
        {
            case 0:
                ShowContentTab();
                break;
            case 1:
                ShowSettingsTab();
                break;
        }
    }

    private void ShowContentTab()
    {
        if (string.IsNullOrEmpty(cookies))
        {
            EditorGUILayout.HelpBox("�N�b�L�[���w�肳��Ă��܂���B�ݒ�^�u�ŃN�b�L�[���w�肵�Ă��������B", MessageType.Error);
        }
        else
        {
            if (GUILayout.Button("Fetch Data"))
            {
                _ = FetchData();
            }
        }

        // �����t�B�[���h�ƃ^�O�\�[�g�A�ǉ��{�^�������ɔz�u
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", EditorStyles.boldLabel);
        searchQuery = EditorGUILayout.TextField(searchQuery);
        selectedTagMask = EditorGUILayout.MaskField(selectedTagMask, tagOptions.ToArray(), GUILayout.Width(200));
        if (GUILayout.Button("+", GUILayout.Width(30)))
        {
            AddNewItemWindow.ShowWindow(this);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);

        // ��������у\�[�g���ꂽ�A�C�e���̃��X�g���擾
        var filteredItems = GetFilteredAndSortedItems();

        // �\������A�C�e���͈̔͂��v�Z
        int startItemIndex = currentPage * ItemsPerPage;
        int endItemIndex = Mathf.Min(startItemIndex + ItemsPerPage, filteredItems.Count);

        if (filteredItems.Count > 0)
        {
            for (int i = startItemIndex; i < endItemIndex; i++)
            {
                var item = filteredItems[i];
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (item.iconTexture != null)
                {
                    GUILayout.Label(item.iconTexture, GUILayout.Width(100), GUILayout.Height(100));
                }
                GUILayout.BeginVertical();
                if (string.IsNullOrEmpty(item.url))
                {
                    GUILayout.Label(EditorGUIUtility.IconContent("console.erroricon.sml"), GUILayout.Width(20), GUILayout.Height(20));
                    GUILayout.Label(item.title, GUILayout.Width(300));
                }
                else
                {
                    if (GUILayout.Button(item.title, GUILayout.Width(300)))
                    {
                        Application.OpenURL(item.url);
                    }
                }
                GUILayout.BeginHorizontal();
                if (item.shopIconTexture != null)
                {
                    GUILayout.Label(item.shopIconTexture, GUILayout.Width(50), GUILayout.Height(50));
                }
                if (GUILayout.Button(item.shopName, GUILayout.Width(300)))
                {
                    Application.OpenURL(item.shopUrl);
                }
                GUILayout.EndHorizontal();

                // �^�O�̃��x�����폜
                int mask = 0;
                for (int j = 0; j < tagOptions.Count; j++)
                {
                    if (item.tags.Contains(tagOptions[j]))
                    {
                        mask |= 1 << j;
                    }
                }
                int newMask = EditorGUILayout.MaskField(mask, tagOptions.ToArray(), GUILayout.Width(200));
                if (newMask != mask)
                {
                    bool avatarTagRemoved = (mask & (1 << tagOptions.IndexOf("�A�o�^�["))) != 0 && (newMask & (1 << tagOptions.IndexOf("�A�o�^�["))) == 0;

                    item.tags.Clear();
                    for (int j = 0; j < tagOptions.Count; j++)
                    {
                        if ((newMask & (1 << j)) != 0)
                        {
                            item.tags.Add(tagOptions[j]);
                        }
                    }

                    if (avatarTagRemoved)
                    {
                        foreach (var download in item.downloads)
                        {
                            if (download.selectedAvatarName == item.title)
                            {
                                download.selectedAvatarName = "";
                            }
                        }

                        foreach (var otherItem in items)
                        {
                            if (otherItem != item)
                            {
                                foreach (var download in otherItem.downloads)
                                {
                                    if (download.selectedAvatarName == item.title)
                                    {
                                        download.selectedAvatarName = "";
                                    }
                                }
                            }
                        }
                    }

                    SaveDataToJson();
                }
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();

                GUILayout.Space(10);

                if (!foldouts.ContainsKey(item.title))
                {
                    foldouts[item.title] = false;
                }

                foldouts[item.title] = EditorGUILayout.Foldout(foldouts[item.title], "Downloads");

                if (foldouts[item.title])
                {
                    foreach (var download in item.downloads)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(download.name, GUILayout.Width(200));

                        List<string> avatarTitles = new List<string>();
                        foreach (var avatarItem in items)
                        {
                            if (avatarItem.tags.Contains("�A�o�^�["))
                            {
                                avatarTitles.Add(avatarItem.title);
                            }
                        }
                        int selectedIndex = avatarTitles.IndexOf(download.selectedAvatarName);
                        int newAvatarIndex = EditorGUILayout.Popup(selectedIndex, avatarTitles.ToArray(), GUILayout.Width(200));
                        if (newAvatarIndex != selectedIndex)
                        {
                            download.selectedAvatarName = avatarTitles[newAvatarIndex];
                            SaveDataToJson();
                        }

                        string filePath = Path.Combine(libraryDirectory, download.name);
                        if (download.isDownloading)
                        {
                            GUILayout.Label("Downloading", GUILayout.Width(100));
                        }
                        else if (download.isUnPackaging)
                        {
                            GUILayout.Label($"UnPackaging", GUILayout.Width(150));
                        }
                        else if (downloadedFiles.Contains(download.name) && File.Exists(filePath))
                        {
                            if (GUILayout.Button("Open Folder", GUILayout.Width(100)))
                            {
                                EditorUtility.RevealInFinder(filePath);
                            }
                            if (!string.IsNullOrEmpty(download.unityPackagePath) && GUILayout.Button("Import", GUILayout.Width(100)))
                            {
                                AssetDatabase.ImportPackage(download.unityPackagePath, true);
                            }
                        }
                        else if (GUILayout.Button("Download", GUILayout.Width(100)))
                        {
                            download.isDownloading = true;
                            _ = DownloadFile(download.link, download.name, download);
                        }

                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(10);
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            }
        }

        GUILayout.EndScrollView(); // ������EndScrollView���Ăяo���܂�

        // �y�[�W���O�{�^��
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Previous", GUILayout.Width(100)) && currentPage > 0)
        {
            currentPage--;
        }
        if (GUILayout.Button("Next", GUILayout.Width(100)) && endItemIndex < filteredItems.Count)
        {
            currentPage++;
        }
        GUILayout.EndHorizontal();
    }

    private void ShowSettingsTab()
    {
        GUILayout.Label("Cookies", EditorStyles.boldLabel);
        cookies = EditorGUILayout.TextArea(cookies, GUILayout.Height(100));

        GUILayout.Label("Work Directory", EditorStyles.boldLabel);
        workDirectory = EditorGUILayout.TextField(workDirectory);

        deleteZipAfterUnpacking = EditorGUILayout.Toggle("Delete Zip After Unpacking", deleteZipAfterUnpacking);

        if (GUILayout.Button("Save Settings"))
        {
            SaveSettings();
        }
    }

    private List<Item> GetFilteredAndSortedItems()
    {
        var filteredItems = items;

        // �����N�G���Ńt�B���^�����O
        if (!string.IsNullOrEmpty(searchQuery))
        {
            filteredItems = filteredItems.FindAll(item => item.title.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // �^�O�Ń\�[�g
        if (selectedTagMask != 0) // "Nothing" �̏ꍇ�͑S�\��
        {
            filteredItems = filteredItems.FindAll(item =>
            {
                int itemMask = 0;
                for (int i = 0; i < tagOptions.Count; i++)
                {
                    if (item.tags.Contains(tagOptions[i]))
                    {
                        itemMask |= 1 << i;
                    }
                }
                return (itemMask & selectedTagMask) != 0;
            });
        }

        return filteredItems;
    }

    private async Task FetchData()
    {
        items.Clear(); // �����̃f�[�^���N���A

        var handler = new HttpClientHandler();
        var cookieContainer = new CookieContainer();
        handler.CookieContainer = cookieContainer;

        // �N�b�L�[��ݒ�
        SetCookies(cookieContainer);

        using (HttpClient client = new HttpClient(handler))
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ja-JP,ja;q=0.9");

            int page = 1;
            bool hasNextPage = true;

            while (hasNextPage)
            {
                string html = await client.GetStringAsync($"{url}?page={page}");

                // �^�C�g���AURL�A�A�C�R���𒊏o���鐳�K�\��
                var itemRegex = new Regex(@".*?<a target=""_blank"" rel=""noopener"" href=""(?<url>.*?)"">.*?<img class=""l-library-item-thumbnail"" src=""(?<icon>.*?)"".*?<div class=""text-text-default font-bold typography-16 !preserve-half-leading mb-8 break-all"">(?<title>.*?)<\/div>.*?<a target=""_blank"" class=""no-underline"" rel=""noopener"" href=""(?<shopUrl>.*?)"">.*?<img alt=""(?<shopName>.*?)"" class=""rounded-\[50%\] w-24 h-24"" src=""(?<shopIcon>.*?)"".*?<div class=""mt-16"">(?<downloads>.*?)<div class=""typography-14 !preserve-half-leading"">�_�E�����[�h</div></a></div></div></div></div>", RegexOptions.Singleline);
                var matches = itemRegex.Matches(html);

                foreach (Match match in matches)
                {
                    string title = match.Groups["title"].Value;
                    string itemUrl = match.Groups["url"].Value;
                    string icon = match.Groups["icon"].Value;
                    string shopUrl = match.Groups["shopUrl"].Value;
                    string shopName = match.Groups["shopName"].Value;
                    string shopIcon = match.Groups["shopIcon"].Value;

                    // �摜���_�E�����[�h����Texture2D�ɕϊ�
                    Texture2D iconTexture = await DownloadImage(icon);
                    Texture2D shopIconTexture = await DownloadImage(shopIcon);

                    // �_�E�����[�h�����N�𒊏o���鐳�K�\��
                    var downloadRegex = new Regex(@"<div class=""min-w-0 break-words whitespace-pre-line""><div class=""typography-14 !preserve-half-leading"">(?<name>.*?)<\/div><\/div>.*?<a.*?href=""(?<link>.*?)""", RegexOptions.Singleline);
                    var downloadMatches = downloadRegex.Matches(match.Groups["downloads"].Value);

                    List<Download> downloads = new List<Download>();
                    foreach (Match downloadMatch in downloadMatches)
                    {
                        string name = downloadMatch.Groups["name"].Value;
                        string link = downloadMatch.Groups["link"].Value;
                        downloads.Add(new Download { name = name, link = link });
                    }

                    var existingItem = items.Find(item => item.url == itemUrl);

                    if (existingItem != null)
                    {
                        // �����̃A�C�e���Ƀ_�E�����[�h��ǉ�
                        existingItem.downloads.AddRange(downloads);
                    }
                    else
                    {
                        var item = new Item
                        {
                            title = title,
                            url = itemUrl,
                            icon = icon,
                            iconTexture = iconTexture,
                            shopUrl = shopUrl,
                            shopName = shopName,
                            shopIcon = shopIcon,
                            shopIconTexture = shopIconTexture,
                            downloads = downloads,
                            tags = new List<string>()
                        };

                        // URL�}�b�s���O�Ɋ�Â��ă^�O��t�^���A���O��ύX
                        ApplyUrlMappings(item);

                        items.Add(item);
                    }
                }

                // ���̃y�[�W�����݂��邩�m�F
                hasNextPage = html.Contains(@"rel=""next""");
                page++;
            }
        }

        SaveDataToJson();
    }

    private void SetCookies(CookieContainer cookieContainer)
    {
        if (!string.IsNullOrEmpty(cookies))
        {
            // ���[�U�[���w�肵���N�b�L�[��ݒ�
            var cookiePairs = cookies.Split(';');
            foreach (var cookiePair in cookiePairs)
            {
                var cookieParts = cookiePair.Split('=');
                if (cookieParts.Length == 2)
                {
                    cookieContainer.Add(new Uri(url), new Cookie(cookieParts[0].Trim(), cookieParts[1].Trim()));
                }
            }
        }
    }

    public async Task<Texture2D> DownloadImage(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl) || !Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
        {
            Debug.Log($"Invalid URL: {imageUrl}");
            return GetErrorIcon();
        }

        using (HttpClient client = new HttpClient())
        {
            var response = await client.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            byte[] imageData = await response.Content.ReadAsByteArrayAsync();

            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(imageData);
            return texture;
        }
    }

    private async Task DownloadFile(string url, string fileName, Download download)
    {
        string filePath = Path.Combine(libraryDirectory, fileName);

        // ���ɑ��݂���t�@�C�����`�F�b�N
        if (File.Exists(filePath))
        {
            Debug.Log("File already exists.");
            downloadedFiles.Add(fileName);
            return;
        }

        string fileDownloaderPath = System.IO.Path.Combine(Application.dataPath, "..", "Packages", "com.github.maiotachannel.assets_library_manager", "FileDownloader.exe");
        string arguments = $"\"{url}\" \"{filePath}\" \"{cookies}\"";

        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileDownloaderPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new System.Diagnostics.Process
        {
            StartInfo = processStartInfo
        };

        var tcs = new TaskCompletionSource<bool>();

        process.Exited += (sender, e) => tcs.SetResult(process.ExitCode == 0);
        process.EnableRaisingEvents = true;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            bool success = await tcs.Task;

            if (success)
            {
                Debug.Log("File downloaded successfully.");
                downloadedFiles.Add(fileName);
                download.isDownloading = false;

                // �𓀏������J�n
                download.isUnPackaging = true;
                await UnpackFile(filePath, download);
                download.isUnPackaging = false;
            }
            else
            {
                Debug.LogError("Failed to download file.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception occurred while downloading file: {ex.Message}");
        }
    }

    private async Task UnpackFile(string filePath, Download download)
    {
        string outputDirectory = Path.Combine(libraryDirectory, Path.GetFileNameWithoutExtension(filePath));

        try
        {
            // �𓀏������J�n
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(filePath, outputDirectory);
            });

            Debug.Log("File unpacked successfully.");

            // �𓀌��.unitypackage�t�@�C��������
            var unityPackageFiles = Directory.GetFiles(outputDirectory, "*.unitypackage", SearchOption.AllDirectories);
            if (unityPackageFiles.Length > 0)
            {
                download.unityPackagePath = unityPackageFiles[0];
            }

            // �𓀌��Zip�t�@�C�����폜
            if (deleteZipAfterUnpacking)
            {
                File.Delete(filePath);
                Debug.Log("Zip file deleted after unpacking.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to unpack file: {ex.Message}");
        }
    }

    public Texture2D GetErrorIcon()
    {
        return EditorGUIUtility.IconContent("console.erroricon.sml").image as Texture2D;
    }


    private void SaveSettings()
    {
        EditorPrefs.SetString("BoothLibrary_Cookies", cookies);
        EditorPrefs.SetString("BoothLibrary_WorkDirectory", workDirectory);
        EditorPrefs.SetBool("BoothLibrary_DeleteZipAfterUnpacking", deleteZipAfterUnpacking);
    }

    private void LoadSettings()
    {
        cookies = EditorPrefs.GetString("BoothLibrary_Cookies", "");
        workDirectory = EditorPrefs.GetString("BoothLibrary_WorkDirectory", Application.dataPath);
        deleteZipAfterUnpacking = EditorPrefs.GetBool("BoothLibrary_DeleteZipAfterUnpacking", false);
    }


    public void SaveDataToJson()
    {
        var itemsToSave = new List<ItemForJson>();

        foreach (var item in items)
        {
            itemsToSave.Add(new ItemForJson
            {
                title = item.title,
                url = item.url,
                icon = item.icon,
                shopUrl = item.shopUrl,
                shopName = item.shopName,
                shopIcon = item.shopIcon,
                downloads = item.downloads,
                tags = item.tags
            });
        }

        string json = JsonConvert.SerializeObject(itemsToSave, Formatting.Indented);
        File.WriteAllText(jsonFilePath, json);
    }

    private async Task LoadDataFromJson()
    {
        items.Clear(); // �����̃f�[�^���N���A

        string json = File.ReadAllText(jsonFilePath);
        var itemsFromJson = JsonConvert.DeserializeObject<List<ItemForJson>>(json);

        foreach (var itemJson in itemsFromJson)
        {
            var item = new Item
            {
                title = itemJson.title,
                url = itemJson.url,
                icon = itemJson.icon,
                iconTexture = await DownloadImage(itemJson.icon), // �摜���ă_�E�����[�h���Đݒ�
                shopUrl = itemJson.shopUrl,
                shopName = itemJson.shopName,
                shopIcon = itemJson.shopIcon,
                shopIconTexture = await DownloadImage(itemJson.shopIcon), // �摜���ă_�E�����[�h���Đݒ�
                downloads = itemJson.downloads,
                tags = itemJson.tags
            };

            // URL�}�b�s���O�Ɋ�Â��ă^�O��t�^���A���O��ύX
            ApplyUrlMappings(item);

            // ���ɑ��݂���t�@�C����F��
            foreach (var download in item.downloads)
            {
                string filePath = Path.Combine(libraryDirectory, download.name);
                if (File.Exists(filePath))
                {
                    downloadedFiles.Add(download.name);

                    // �𓀌��.unitypackage�t�@�C���̃p�X��ݒ�
                    string outputDirectory = Path.Combine(libraryDirectory, Path.GetFileNameWithoutExtension(filePath));
                    var unityPackageFiles = Directory.GetFiles(outputDirectory, "*.unitypackage", SearchOption.AllDirectories);
                    if (unityPackageFiles.Length > 0)
                    {
                        download.unityPackagePath = unityPackageFiles[0];
                    }
                }
            }

            items.Add(item);
        }
    }


    private void LoadUrlMappings()
    {
        if (File.Exists(urlMappingsFilePath))
        {
            string json = File.ReadAllText(urlMappingsFilePath);
            urlMappings = JsonConvert.DeserializeObject<List<UrlMapping>>(json);
        }
    }

    private void ApplyUrlMappings(Item item)
    {
        foreach (var mapping in urlMappings)
        {
            if (item.url.Contains(mapping.url))
            {
                if (!item.tags.Contains(mapping.tag))
                {
                    item.tags.Add(mapping.tag);
                }
                if (!string.IsNullOrEmpty(mapping.newName))
                {
                    item.title = mapping.newName;
                }
            }
        }
    }

    [Serializable]
    public class Item
    {
        public string title;
        public string url;
        public string icon;
        public Texture2D iconTexture;
        public string shopUrl;
        public string shopName;
        public string shopIcon;
        public Texture2D shopIconTexture;
        public List<Download> downloads;
        public List<string> tags;
    }

    [Serializable]
    private class ItemForJson
    {
        public string title;
        public string url;
        public string icon;
        public string shopUrl;
        public string shopName;
        public string shopIcon;
        public List<Download> downloads;
        public List<string> tags;
    }

    [Serializable]
    public class Download
    {
        public string name;
        public string link;
        public string selectedAvatarName = ""; // �_�E�����[�h���ƂɑI�����ꂽ�A�o�^�[�̖��O��ێ�
        public bool isDownloading = false; // �_�E�����[�h�����ǂ����������t���O
        public bool isUnPackaging = false; // �𓀒����ǂ����������t���O
        public string unityPackagePath = ""; // �𓀌��.unitypackage�t�@�C���̃p�X
    }

    [Serializable]
    private class UrlMapping
    {
        public string url;
        public string tag;
        public string newName;
    }
}

public class AddNewItemWindow : EditorWindow
{
    private AssetLibraryManager parentWindow;
    private string newItemTitle = "";
    private string newItemUrl = "";
    private string newItemIconUrl = "";
    private string newItemShopUrl = "";
    private string newItemShopName = "";
    private string newItemShopIconUrl = "";
    private List<string> newItemTags = new List<string>();
    private List<Download> newItemDownloads = new List<Download>();
    private string newDownloadName = "";
    private string newDownloadLink = "";

    public static void ShowWindow(AssetLibraryManager parent)
    {
        var window = GetWindow<AddNewItemWindow>("Add New Item");
        window.parentWindow = parent;
    }

    private void OnGUI()
    {
        GUILayout.Label("Add New Item", EditorStyles.boldLabel);

        newItemTitle = EditorGUILayout.TextField("Title", newItemTitle);
        newItemUrl = EditorGUILayout.TextField("URL", newItemUrl);
        newItemIconUrl = EditorGUILayout.TextField("Icon URL", newItemIconUrl);
        newItemShopUrl = EditorGUILayout.TextField("Shop URL", newItemShopUrl);
        newItemShopName = EditorGUILayout.TextField("Shop Name", newItemShopName);
        newItemShopIconUrl = EditorGUILayout.TextField("Shop Icon URL", newItemShopIconUrl);

        GUILayout.Label("Tags", EditorStyles.boldLabel);
        for (int i = 0; i < parentWindow.tagOptions.Count; i++)
        {
            bool isSelected = newItemTags.Contains(parentWindow.tagOptions[i]);
            bool newIsSelected = EditorGUILayout.ToggleLeft(parentWindow.tagOptions[i], isSelected);
            if (newIsSelected && !isSelected)
            {
                newItemTags.Add(parentWindow.tagOptions[i]);
            }
            else if (!newIsSelected && isSelected)
            {
                newItemTags.Remove(parentWindow.tagOptions[i]);
            }
        }

        GUILayout.Label("Downloads", EditorStyles.boldLabel);
        newDownloadName = EditorGUILayout.TextField("Download Name", newDownloadName);
        newDownloadLink = EditorGUILayout.TextField("Download Link", newDownloadLink);

        if (GUILayout.Button("Add Download"))
        {
            newItemDownloads.Add(new Download { name = newDownloadName, link = newDownloadLink });
            newDownloadName = "";
            newDownloadLink = "";
        }

        foreach (var download in newItemDownloads)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(download.name, GUILayout.Width(200));
            GUILayout.Label(download.link, GUILayout.Width(400));
            if (GUILayout.Button("Remove", GUILayout.Width(100)))
            {
                newItemDownloads.Remove(download);
                break;
            }
            GUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Add Item"))
        {
            AddNewItem();
        }
    }

    private async void AddNewItem()
    {
        Texture2D iconTexture = await parentWindow.DownloadImage(newItemIconUrl);
        Texture2D shopIconTexture = await parentWindow.DownloadImage(newItemShopIconUrl);

        var existingItem = parentWindow.items.Find(item => item.url == newItemUrl);

        if (existingItem != null)
        {
            // �����̃A�C�e���Ƀ_�E�����[�h��ǉ�
            existingItem.downloads.AddRange(newItemDownloads);
        }
        else
        {
            var newItem = new AssetLibraryManager.Item
            {
                title = newItemTitle,
                url = newItemUrl,
                icon = newItemIconUrl,
                iconTexture = iconTexture ?? parentWindow.GetErrorIcon(),
                shopUrl = newItemShopUrl,
                shopName = newItemShopName,
                shopIcon = newItemShopIconUrl,
                shopIconTexture = shopIconTexture ?? parentWindow.GetErrorIcon(),
                downloads = newItemDownloads,
                tags = newItemTags
            };

            parentWindow.items.Add(newItem);
        }

        parentWindow.SaveDataToJson();
        Close();
    }

}