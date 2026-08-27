using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BaboonAPI.Hooks.Tracks;
using Newtonsoft.Json;
using TMPro;
using TootTallyAccounts;
using TootTallyCore;
using TootTallyCore.APIServices;
using TootTallyCore.Graphics;
using TootTallyCore.Graphics.Animations;
using TootTallyCore.Utils.Assets;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyGlobals;
using TootTallyCore.Utils.TootTallyNotifs;
using TootTallyDiffCalcLibs;
using TootTallyLeaderboard.Replays;
using TrombLoader.CustomTracks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TootTallyLeaderboard
{
    public class GlobalLeaderboard
    {
        #region constants
        private const string ERROR_NO_LEADERBOARD_FOUND_TEXT = "Could not find a leaderboard for this track.\n <size=15>Be the first one to set a score on the track!</size>"; //lol
        private const string ERROR_NO_SONGHASH_FOUND_TEXT = "This chart is not uploaded to TootTally...\n <size=15>Please upload the chart to TootTally.com\n or use TootBender on discord to create the leaderboard.</size>";
        private static Dictionary<string, Color> gradeToColorDict = new Dictionary<string, Color> { { "SSS", Color.yellow }, { "SS", Color.yellow }, { "S", Color.yellow }, { "A", Color.green }, { "B", new Color(0, .4f, 1f) }, { "C", Color.magenta }, { "D", Color.red }, { "F", Color.grey }, };
        private static string[] tabsImageNames = { "profile64.png", "global64.png", "local64.png" };
        private static float[] _starSizeDeltaPositions = { 0, 19, 39, 59, 79, 99, 119, 139, 159, 179, 199 };
        private static GameObject _fullScreenPanelCanvas;
        #endregion

        private List<IEnumerator<UnityWebRequestAsyncOperation>> _currentLeaderboardCoroutines;
        private IEnumerator<UnityWebRequestAsyncOperation> _profilePictureRequest;

        private LevelSelectController _levelSelectControllerInstance;

        private List<SerializableClass.ScoreDataFromDB> _scoreDataList;

        private GameObject _leaderboard, _globalLeaderboard, _scoreboard, _errorsHolder, _tabs, _profilePopup;
        private GameObject _ratedIcon;
        private LoadingIcon _loadingSwirly, _profilePopupLoadingSwirly;
        private Text _errorText;
        private TMP_Text _diffRating;
        private Vector2 _starRatingMaskSizeTarget;
        private RectTransform _diffRatingMaskRectangle;
        private List<LeaderboardRowEntry> _scoreGameObjectList;
        private List<SerializableClass.ScoreDataFromDB> _tempAllReplayData;
        private SerializableClass.SongDataFromDB _songData;
        //private Chart _localSongData;
        private Slider _slider, _gameSpeedSlider;
        private ScrollableSliderHandler _scrollableSliderHandler;
        private GameObject _sliderHandle;

        private Dictionary<int, float> _speedToDiffDict;

        private int _currentSelectedSongHash, _localScoreId;
        private string _songHash;
        public bool HasLeaderboard => _leaderboard != null;
        public bool IsMouseOver;

        private SecondDegreeDynamicsAnimation _starMaskAnimation;

        public void Initialize(LevelSelectController __instance)
        {
            _fullScreenPanelCanvas = GameObject.Find("MainCanvas/FullScreenPanel");
            _levelSelectControllerInstance = __instance;
            _currentLeaderboardCoroutines = new List<IEnumerator<UnityWebRequestAsyncOperation>>();
            _scoreGameObjectList = new List<LeaderboardRowEntry>();
            _tempAllReplayData = new List<SerializableClass.ScoreDataFromDB>();
            _speedToDiffDict = new Dictionary<int, float>();
            ClearBaseLeaderboard();
            CustomizeGameMenuUI(__instance);

            _leaderboard.transform.SetAsLastSibling();
            _globalLeaderboard = LeaderboardFactory.CreateSteamLeaderboardFromPrefab(_leaderboard.transform, "GlobalLeaderboard");
            _globalLeaderboard.SetActive(true);

            EventTrigger leaderboardHitboxEvent = _globalLeaderboard.AddComponent<EventTrigger>();
            EventTrigger.Entry pointerEnterEvent = new EventTrigger.Entry();
            pointerEnterEvent.eventID = EventTriggerType.PointerEnter;
            pointerEnterEvent.callback.AddListener((data) => OnPointerEnter());
            leaderboardHitboxEvent.triggers.Add(pointerEnterEvent);

            EventTrigger.Entry pointerExitEvent = new EventTrigger.Entry();
            pointerExitEvent.eventID = EventTriggerType.PointerExit;
            pointerExitEvent.callback.AddListener((data) => OnPointerExit());
            leaderboardHitboxEvent.triggers.Add(pointerExitEvent);


            GameObject panelBody = _globalLeaderboard.transform.Find("PanelBody").gameObject;
            panelBody.SetActive(true);
            _scoreboard = panelBody.transform.Find("scoreboard").gameObject;
            _scoreboard.SetActive(true);

            _errorsHolder = panelBody.transform.Find("errors").gameObject;

            _errorText = _errorsHolder.transform.Find("error_noleaderboard").GetComponent<Text>();
            _errorText.gameObject.SetActive(true);

            _tabs = panelBody.transform.Find("tabs").gameObject; //Hidden until icons are loaded
            SetTabsImages();

            _loadingSwirly = GameObjectFactory.CreateLoadingIcon(panelBody.transform, new Vector2(-20, 0), new Vector2(128, 128), AssetManager.GetSprite("icon.png"), true, "LeaderboardLoadingSwirly");
            _loadingSwirly.StartRecursiveAnimation();
            _loadingSwirly.Show();

            _slider = panelBody.transform.Find("LeaderboardVerticalSlider").gameObject.GetComponent<Slider>();
            _slider.transform.Find("Fill Area/Fill").GetComponent<Image>().color = Theme.colors.leaderboard.slider.fill;
            _slider.transform.Find("Background").GetComponent<Image>().color = Theme.colors.leaderboard.slider.background;

            _scrollableSliderHandler = _slider.gameObject.AddComponent<ScrollableSliderHandler>();

            _sliderHandle = _slider.transform.Find("Handle").gameObject;
            _sliderHandle.GetComponent<Image>().color = Theme.colors.leaderboard.slider.handle;

            SetOnSliderValueChangeEvent();

            GameObject diffBar = _fullScreenPanelCanvas.transform.Find("bar_difficulty").gameObject;
            GameObject.DestroyImmediate(_fullScreenPanelCanvas.transform.Find("difficulty text").gameObject);
            var t = GameObjectFactory.CreateSingleText(diffBar.transform, "Difficulty Text", "Difficulty:", GameObjectFactory.TextFont.Multicolore);
            t.alignment = TextAlignmentOptions.Left;
            t.margin = new Vector2(20, 4);
            t.fontSize = 16;

            GameObject diffStarsHolder = _fullScreenPanelCanvas.transform.Find("difficulty stars").gameObject;
            _diffRatingMaskRectangle = diffStarsHolder.GetComponent<RectTransform>();
            _diffRatingMaskRectangle.anchoredPosition = new Vector2(120, -48);
            _diffRatingMaskRectangle.sizeDelta = new Vector2(0, 30);
            _diffRatingMaskRectangle.pivot = new Vector2(0, .5f);
            var mask = diffStarsHolder.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            diffStarsHolder.AddComponent<Image>();
            diffBar.GetComponent<RectTransform>().sizeDelta += new Vector2(41.5f, 0);
            _diffRating = GameObjectFactory.CreateSingleText(diffBar.transform, "diffRating", "", GameObjectFactory.TextFont.Multicolore);
            _diffRating.outlineColor = Theme.colors.leaderboard.textOutline;
            _diffRating.outlineWidth = 0.2f;
            _diffRating.fontSize = 20;
            _diffRating.alignment = TextAlignmentOptions.MidlineRight;
            _diffRating.rectTransform.sizeDelta = new Vector2(355, 30);
            _diffRating.rectTransform.anchorMin = _diffRating.rectTransform.anchorMax = new Vector2(0, .5f);
            _diffRating.rectTransform.offsetMin = Vector2.zero;

            _starMaskAnimation = new SecondDegreeDynamicsAnimation(1.23f, 1f, 1.2f);

            _ratedIcon = GameObjectFactory.CreateImageHolder(_globalLeaderboard.transform, new Vector2(350, 180), Vector2.one * 42f, AssetManager.GetSprite("rated64.png"), "RatedChartIcon");
            var bubble = _ratedIcon.AddComponent<BubblePopupHandler>();
            bubble.Initialize(GameObjectFactory.CreateBubble(new Vector2(300, 40), "RatedIconBubble", "This chart is rated.", 6, true, 12));

            _levelSelectControllerInstance.sortdrop.transform.SetAsLastSibling();
        }

        public void OnPointerEnter()
        {
            _scrollableSliderHandler.enabled = IsMouseOver = true;
        }

        public void OnPointerExit()
        {
            _scrollableSliderHandler.enabled = IsMouseOver = false;
        }

        public void ClearBaseLeaderboard()
        {
            _leaderboard = _fullScreenPanelCanvas.transform.Find("Leaderboard").gameObject;

            //clear original Leaderboard from its objects
            foreach (Transform gameObjectTransform in _leaderboard.transform)
                gameObjectTransform.gameObject.SetActive(false);

            DestroyFromParent(_leaderboard, ".......");
            DestroyFromParent(_leaderboard, "\"HIGH SCORES\"");
            for (int i = 1; i <= 5; i++)
                _leaderboard.transform.Find(i.ToString()).gameObject.SetActive(false);
        }

        public void CustomizeGameMenuUI(LevelSelectController __instance)
        {
            try
            {
                //fuck that useless Dial
                _fullScreenPanelCanvas.transform.Find("Dial").gameObject.SetActive(false);

                //move capsules to the left
                _fullScreenPanelCanvas.transform.Find("capsules").GetComponent<RectTransform>().anchoredPosition = new Vector2(-275, 32);

                //move btn_random next to capsules
                _fullScreenPanelCanvas.transform.Find("btn_RANDOM").GetComponent<RectTransform>().anchoredPosition = new Vector2(-123, -7);

                //move btn_turbo somewhere
                _fullScreenPanelCanvas.transform.Find("btn_TURBO").GetComponent<RectTransform>().anchoredPosition = new Vector2(-110, 65);

                //Patch current slider and move it slightly above RANDOM_btn
                BetterScrollSpeedSliderPatcher.PatchScrollSpeedSlider();
                _fullScreenPanelCanvas.transform.Find("scroll_speed").GetComponent<RectTransform>().anchoredPosition = new Vector2(4.5f, 5f);
                _fullScreenPanelCanvas.transform.Find("scroll_speed/Slider").GetComponent<RectTransform>().anchoredPosition = new Vector2(-115f, 22f);
                _fullScreenPanelCanvas.transform.Find("scroll_speed/txt_scrollspeed").GetComponent<RectTransform>().anchoredPosition = new Vector2(-112, 36);

                //Remove btn_TURBO + btn_PRACTICE and add GameSpeed slider
                _fullScreenPanelCanvas.transform.Find("btn_TURBO").gameObject.SetActive(false);
                _fullScreenPanelCanvas.transform.Find("btn_PRACTICE").gameObject.SetActive(false);
                _gameSpeedSlider = GameObject.Instantiate(_fullScreenPanelCanvas.transform.Find("scroll_speed/Slider").GetComponent<Slider>(), _fullScreenPanelCanvas.transform);
                _gameSpeedSlider.gameObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(-110, 65);
                _gameSpeedSlider.wholeNumbers = true;
                _gameSpeedSlider.minValue = 0;
                _gameSpeedSlider.maxValue = 30;
                _gameSpeedSlider.value = (Replays.ReplaySystemManager.gameSpeedMultiplier - .5f) / .05f;

                GameObject gameSpeedText = GameObject.Instantiate(_fullScreenPanelCanvas.transform.Find("scroll_speed/txt_scrollspeed").gameObject, _fullScreenPanelCanvas.transform);
                gameSpeedText.name = "GameSpeedShad";
                gameSpeedText.GetComponent<Text>().text = "Game Speed";
                gameSpeedText.GetComponent<RectTransform>().anchoredPosition = new Vector2(-108, 78);
                GameObject gameSpeedTextFG = gameSpeedText.transform.Find("txt_scrollspeed-top").gameObject;
                gameSpeedTextFG.name = "GameSpeed";
                gameSpeedTextFG.GetComponent<Text>().text = "Game Speed";

                Text scrollSpeedSliderText = _gameSpeedSlider.transform.Find("Handle Slide Area/Handle/ScrollSpeed-lbl(Clone)").GetComponent<Text>(); //💀
                scrollSpeedSliderText.text = (_gameSpeedSlider.value * .05f + .5f).ToString("0.00");
                _gameSpeedSlider.onValueChanged = new Slider.SliderEvent();
                _gameSpeedSlider.onValueChanged.AddListener((float _value) =>
                {
                    _gameSpeedSlider.value = Mathf.Round(_value * 20) / 20f;
                    TootTallyGlobalVariables.gameSpeedMultiplier = _gameSpeedSlider.value * .05f + .5f;
                    scrollSpeedSliderText.text = Replays.ReplaySystemManager.gameSpeedMultiplier.ToString("0.00");
                    UpdateStarRating(__instance);
                });

                GameObject titlebarPrefab = GameObject.Instantiate(_levelSelectControllerInstance.songtitlebar);
                titlebarPrefab.name = "titlebarPrefab";
                titlebarPrefab.GetComponent<RectTransform>().eulerAngles = Vector3.zero;
                titlebarPrefab.GetComponent<RectTransform>().localScale = Vector3.one;
                titlebarPrefab.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);


                GameObject ttHitbox = LeaderboardFactory.CreateDefaultPanel(_fullScreenPanelCanvas.transform, new Vector2(381, -207), new Vector2(72, 72), "ProfilePopupHitbox");
                GameObjectFactory.CreateSingleText(ttHitbox.transform, "ProfilePopupHitboxText", "P", GameObjectFactory.TextFont.Multicolore);

                if (TootTallyUser.userInfo.id != 0)
                {
                    _profilePopup = LeaderboardFactory.CreateDefaultPanel(_fullScreenPanelCanvas.transform, new Vector2(525, -300), new Vector2(450, 270), "TootTallyScorePanel");
                    _profilePopupLoadingSwirly = GameObjectFactory.CreateLoadingIcon(_profilePopup.transform, Vector2.zero, new Vector2(96, 96), AssetManager.GetSprite("icon.png"), true, "ProfilePopupLoadingSwirly");
                    _profilePopupLoadingSwirly.Show();
                    _profilePopupLoadingSwirly.StartRecursiveAnimation();

                    var scoresbody = _profilePopup.transform.Find("scoresbody").gameObject;

                    HorizontalLayoutGroup horizontalLayoutGroup = scoresbody.AddComponent<HorizontalLayoutGroup>();
                    horizontalLayoutGroup.padding = new RectOffset(2, 2, 2, 2);
                    horizontalLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
                    horizontalLayoutGroup.childForceExpandHeight = horizontalLayoutGroup.childForceExpandWidth = true;

                    GameObject mainPanel = GameObject.Instantiate(titlebarPrefab, scoresbody.transform);
                    mainPanel.GetComponent<Image>().enabled = false;
                    VerticalLayoutGroup verticalLayoutGroup = mainPanel.AddComponent<VerticalLayoutGroup>();
                    verticalLayoutGroup.padding = new RectOffset(2, 2, 2, 2);
                    verticalLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
                    verticalLayoutGroup.childForceExpandHeight = verticalLayoutGroup.childForceExpandWidth = true;

                    _profilePictureRequest = TootTallyAPIService.GetUserFromID(TootTallyUser.userInfo.id, user =>
                    {
                        AssetManager.GetProfilePictureByID(user.id, sprite =>
                        {
                            var i = GameObjectFactory.CreateCustomButton(scoresbody.transform, Vector2.zero, new Vector2(sprite.rect.width, sprite.rect.height), sprite, false, "Pfp", delegate { OpenUserProfile(TootTallyUser.userInfo.id); });
                            i.transform.SetSiblingIndex(0);
                        });

                        if (Plugin.Instance.option.SessionStartTT.Value == 0 || Plugin.Instance.ShouldUpdateSession)
                        {
                            Plugin.Instance.ShouldUpdateSession = false;
                            Plugin.Instance.option.SessionDate.Value = DateTime.Now.AddDays(1).ToString(CultureInfo.InvariantCulture);
                            Plugin.Instance.option.SessionStartTT.Value = user.tt;
                        }

                        var sessionTT = user.tt - Plugin.Instance.option.SessionStartTT.Value;
                        var t = GameObjectFactory.CreateSingleText(mainPanel.transform, "NameLabel", $"{user.username} #{user.rank}");
                        var t2 = GameObjectFactory.CreateSingleText(mainPanel.transform, "TTLabel", $"{user.tt}tt (<color=\"green\">{(sessionTT > 0 ? "+" : "")}{sessionTT:0.00}tt</color>)");
                        _profilePopupLoadingSwirly.Dispose();
                        _profilePictureRequest = null;
                    });
                    Plugin.Instance.StartCoroutine(_profilePictureRequest);

                    new SlideTooltip(ttHitbox, _profilePopup, new Vector2(525, -300), new Vector2(282, -155));
                }
            }
            catch (Exception e)
            {
                Plugin.LogError(e.Message);
            }
        }

        private void UpdateStarRating(LevelSelectController __instance)
        {
            for (int i = 0; i < 10; i++)
            {
                if (!Plugin.Instance.option.ShowLeaderboard.Value && i >= __instance.alltrackslist[__instance.songindex].difficulty) break;

                if (!Theme.isDefault)
                    __instance.diffstars[i].color = Color.Lerp(Theme.colors.diffStar.gradientStart, Theme.colors.diffStar.gradientEnd, i / 9f);
                else
                    __instance.diffstars[i].color = Color.white;

                if (Plugin.Instance.option.ShowLeaderboard.Value)
                {
                    var rect = __instance.diffstars[i].gameObject.GetComponent<RectTransform>();
                    var rect2 = __instance.diffstars[i].transform.parent.gameObject.GetComponent<RectTransform>();
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect2.anchorMin = rect2.anchorMax = new Vector2(0, .5f);
                    __instance.diffstars[i].maskable = true;
                }
            }

            float diff;
            if (_songData != null)
            {
                diff = _speedToDiffDict[(int)_gameSpeedSlider.value];
                _diffRating.text = diff.ToString("0.0");
            }
            else
            {
                diff = DiffCalcGlobals.selectedChart.GetDiffRating(TootTallyGlobalVariables.gameSpeedMultiplier);
                _diffRating.text = $"~{diff:0.0}";
            }
            int roundedUpStar = (int)Mathf.Clamp(diff + 1, 1, 10);
            int roundedDownStar = (int)Mathf.Clamp(diff, 0, 9);
            _starMaskAnimation.SetStartVector(_diffRatingMaskRectangle.sizeDelta);
            _starRatingMaskSizeTarget = new Vector2(EasingHelper.Lerp(_starSizeDeltaPositions[roundedUpStar], _starSizeDeltaPositions[roundedDownStar], roundedUpStar - diff), 30);
        }

        private static string _savedTrackref;

        public void UpdateLeaderboard(LevelSelectController __instance, List<SingleTrackData> ___alltrackslist, Action<LeaderboardState> callback)
        {
            _globalLeaderboard.SetActive(true); //for some reasons its needed to display the leaderboard
            _ratedIcon.SetActive(false);
            _scrollableSliderHandler.ResetAcceleration();

            _savedTrackref = ___alltrackslist[_levelSelectControllerInstance.songindex].trackref;
            var track = TrackLookup.lookup(_savedTrackref);
            _songHash = SongDataHelper.GetSongHash(track);


            if (_currentLeaderboardCoroutines.Count != 0) CancelAndClearAllCoroutineInList();

            _songData = null;
            _scoreDataList = null;

            _currentLeaderboardCoroutines.Add(TootTallyAPIService.GetHashInDB(_songHash, track is CustomTrack, songHashInDB =>
            {

                if (songHashInDB == 0)
                {
                    _errorText.text = ERROR_NO_SONGHASH_FOUND_TEXT;
                    Plugin.LogInfo($" - {DiffCalcGlobals.selectedChart.trackRef}");
                    if (DiffCalcGlobals.selectedChart.trackRef != "")
                        _diffRating.text = $"~{DiffCalcGlobals.selectedChart.GetDiffRating(TootTallyGlobalVariables.gameSpeedMultiplier):0.0}";
                    else
                        _diffRating.text = "NA";
                    UpdateStarRating(__instance);
                    callback(LeaderboardState.ErrorNoSongHashFound);
                    return; // Skip if no song found
                }
                else
                    _currentSelectedSongHash = songHashInDB;
                _currentLeaderboardCoroutines.Add(TootTallyAPIService.GetSongDataFromDB(songHashInDB, songData =>
                {
                    if (songData != null)
                        OnSongInfoReceived(songData);
                    UpdateStarRating(__instance);


                    if (_scoreDataList != null)
                        CancelAndClearAllCoroutineInList();
                }));
                Plugin.Instance.StartCoroutine(_currentLeaderboardCoroutines.Last());
                _currentLeaderboardCoroutines.Add(TootTallyAPIService.GetLeaderboardScoresFromDB(songHashInDB, scoreDataList =>
                {
                    if (scoreDataList != null)
                    {
                        _scoreDataList = scoreDataList;
                        _scrollableSliderHandler.accelerationMult = 8f / _scoreDataList.Count;
                        callback(LeaderboardState.ReadyToRefresh);
                    }
                    else
                    {
                        _errorText.text = ERROR_NO_LEADERBOARD_FOUND_TEXT;
                        callback(LeaderboardState.ErrorNoLeaderboardFound);
                    }

                    if (_songData != null)
                        CancelAndClearAllCoroutineInList();
                }));
                Plugin.Instance.StartCoroutine(_currentLeaderboardCoroutines.Last());
            }));
            Plugin.Instance.StartCoroutine(_currentLeaderboardCoroutines.Last());
        }

        public void OnSongInfoReceived(SerializableClass.SongDataFromDB songData)
        {
            _songData = songData;
            _speedToDiffDict.Clear();
            _ratedIcon.SetActive(songData.is_rated);
            for (int i = 0; i <= 29; i++)
            {
                float diffIndex = (int)(i / 5f);
                float diffMin = diffIndex * .25f + .5f;
                float diffMax = (diffIndex + 1f) * .25f + .5f;
                float currentGameSpeed = i * .05f + .5f;

                float by = (currentGameSpeed - diffMin) / (diffMax - diffMin);

                float diff = EasingHelper.Lerp(_songData.speed_diffs[(int)diffIndex], _songData.speed_diffs[(int)diffIndex + 1], by);

                _speedToDiffDict.Add(i, diff);
            }
            _speedToDiffDict.Add(30, _songData.speed_diffs.Last());
        }

        public void ShowLoadingSwirly() => _loadingSwirly.Show();
        public void HideLoadingSwirly() => _loadingSwirly.Hide();

        public void RefreshLeaderboard()
        {
            var count = 1;
            _localScoreId = -1;
            foreach (SerializableClass.ScoreDataFromDB scoreData in _scoreDataList)
            {
                LeaderboardRowEntry rowEntry = LeaderboardFactory.CreateLeaderboardRowEntryFromScore(_scoreboard.transform, $"RowEntry{scoreData.player}", scoreData, count, gradeToColorDict[scoreData.grade], _levelSelectControllerInstance);
                _scoreGameObjectList.Add(rowEntry);
                if (scoreData.player == TootTallyUser.userInfo.username)
                {
                    rowEntry.imageStrip.color = Theme.colors.leaderboard.yourRowEntry;
                    rowEntry.imageStrip.gameObject.SetActive(true);
                    _localScoreId = count - 1;
                }
                count++;
            }
            if (_scoreGameObjectList.Count > 8)
            {
                _slider.value = 0f;
                _sliderHandle.GetComponent<RectTransform>().anchoredPosition = new Vector2(-12, 522);
                ShowSlider();
            }
            else
                HideSlider();
        }

        //THIS IS JUST IN TESTING PHASE, NOT TO BE USED
        public void StartLocalLeaderboardRoutine()
        {
            _currentLeaderboardCoroutines.Add(RefreshLeaderboardLocal());
            Plugin.Instance.StartCoroutine(_currentLeaderboardCoroutines.Last());
        }

        public IEnumerator<UnityWebRequestAsyncOperation> RefreshLeaderboardLocal()
        {
            ShowLoadingSwirly();
            var count = 1;
            _localScoreId = -1;
            _tempAllReplayData.Clear();
            var cachedReplays = CachedReplays.GetReplayPathsFromSongHash(_songHash);
            var savedSongHash = _songHash;
            foreach (var cachedData in cachedReplays)
            {
                if (_songHash != savedSongHash) yield break;
                if (!File.Exists(cachedData.filePath))
                {
                    Plugin.LogError($"File {cachedData.filePath} didn't exist. Deleting from cache.");
                    CachedReplays.DeleteReplayFromSongHash(cachedData);
                    continue;
                }
                try
                {
                    var replayData = JsonConvert.DeserializeObject<NewReplaySystem.ReplayData>(FileHelper.ReadJsonFromFile("", cachedData.filePath));
                    var percent = (float)replayData.finalscore / DiffCalcGlobals.selectedChart.maxScore;
                    var gamePercent = (float)replayData.finalscore / DiffCalcGlobals.selectedChart.gameMaxScore;
                    var convertedData = new SerializableClass.ScoreDataFromDB()
                    {
                        grade = GetGradeFromLocalReplay(replayData.finalnotetallies, gamePercent),
                        is_rated = _songData.is_rated,
                        max_combo = replayData.maxcombo,
                        modifiers = replayData.gamemodifiers.Length == 0 || replayData.gamemodifiers.ToUpper().Contains("NONE") ? null : replayData.gamemodifiers.Split(','),
                        percentage = percent * 100f,
                        tt = GetTTFromLocalReplay(replayData, percent),
                        score = replayData.finalscore,
                        replay_speed = replayData.gamespeedmultiplier,
                        perfect = replayData.finalnotetallies[0],
                        nice = replayData.finalnotetallies[1],
                        okay = replayData.finalnotetallies[2],
                        meh = replayData.finalnotetallies[3],
                        nasty = replayData.finalnotetallies[4],
                        missed_gaps = replayData.notedata.Count(x => x[(int)NewReplaySystem.NDStruct.R] == 0),
                        game_version = replayData.gameversion,
                        replay_id = replayData.uuid,
                        player = replayData.username,
                        played_on = "",
                        player_id = 1,
                    };
                    _tempAllReplayData.Add(convertedData);
                }
                catch (Exception)
                {
                    Plugin.LogError($"Couldn't parse  replay {cachedData.filePath}.");
                    CachedReplays.DeleteReplayFromSongHash(cachedData);
                }
                yield return null;
            }
            HideLoadingSwirly();
            foreach (var replayData in _tempAllReplayData.OrderByDescending(r => r.tt))
            {
                if (_songHash != savedSongHash) yield break;

                LeaderboardRowEntry rowEntry = LeaderboardFactory.CreateLeaderboardRowEntryFromScore(_scoreboard.transform, $"RowEntry{replayData.player}", replayData, count, gradeToColorDict[replayData.grade], _levelSelectControllerInstance);
                _scoreGameObjectList.Add(rowEntry);
                if (replayData.player == TootTallyUser.userInfo.username)
                {
                    rowEntry.imageStrip.color = Theme.colors.leaderboard.yourRowEntry;
                    rowEntry.imageStrip.gameObject.SetActive(true);
                    _localScoreId = count - 1;
                }
                count++;
                yield return null;
            }
            if (_scoreGameObjectList.Count > 8)
            {
                _slider.value = 0f;
                _sliderHandle.GetComponent<RectTransform>().anchoredPosition = new Vector2(-12, 522);
                ShowSlider();
            }
            else
                HideSlider();
        }

        public float GetTTFromLocalReplay(NewReplaySystem.ReplayData data, float percent) =>
            TootTallyDiffCalcLibs.Utils.CalculateScoreTT(DiffCalcGlobals.selectedChart, data.gamespeedmultiplier, data.finalnotetallies[0] + data.finalnotetallies[1], data.finalnotetallies.Sum(), percent, data.gamemodifiers.Split(','));

        public string GetGradeFromLocalReplay(int[] tally, float gamePercent)
        {
            var noteCount = DiffCalcGlobals.selectedChart.GetNoteCount();
            if (tally[0] >= noteCount) return "SSS";
            else if (tally[4] + tally[3] + tally[2] == 0) return "SS";
            else if (gamePercent >= 1f) return "S";
            else if (gamePercent >= .8f) return "A";
            else if (gamePercent >= .6f) return "B";
            else if (gamePercent >= .4f) return "C";
            else if (gamePercent >= .2f) return "D";
            return "F";
        }

        public void SetOnSliderValueChangeEvent()
        {
            _slider.onValueChanged.AddListener((float _value) =>
            {
                _slider.value = Mathf.Clamp(_value, 0, 1);

                foreach (LeaderboardRowEntry row in _scoreGameObjectList)
                {
                    _sliderHandle.GetComponent<RectTransform>().anchoredPosition = new Vector2(-_slider.GetComponent<RectTransform>().sizeDelta.x / 2, (_slider.GetComponent<RectTransform>().sizeDelta.y / 1.38f) - _slider.fillRect.rect.height); //Dont even ask why divided by 1.38... I dont understand either
                    RectTransform rect = row.singleScore.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, ((row.rowId - 1) * -35) + (_slider.value * 35 * (_scoreGameObjectList.Count - 8)) - 17);
                    if (rect.anchoredPosition.y >= -15)
                        row.GetComponent<CanvasGroup>().alpha = Math.Max(1 - ((rect.anchoredPosition.y + 15) / 35), 0);
                    else if (rect.anchoredPosition.y - 35 <= 35 * 8 + 15)
                        row.GetComponent<CanvasGroup>().alpha = Math.Max((rect.anchoredPosition.y + (35 * 8) + 15) / 35, 0);
                    else
                        row.GetComponent<CanvasGroup>().alpha = 1;
                }
            });
        }

        public void UpdateStarRatingAnimation()
        {
            _diffRatingMaskRectangle.sizeDelta = _starMaskAnimation.GetNewVector(_starRatingMaskSizeTarget, Time.deltaTime);
        }

        public bool IsScrollingEnabled() => _scrollableSliderHandler.enabled;

        public void ClearLeaderboard()
        {
            _scoreGameObjectList.ForEach(score => GameObject.DestroyImmediate(score.singleScore));
            _scoreGameObjectList.Clear();
        }

        public void CancelProfilePictureRequest()
        {
            if (_profilePictureRequest == null) return;
            Plugin.Instance.StopCoroutine(_profilePictureRequest);
        }

        public void CancelAndClearAllCoroutineInList()
        {
            if (_currentLeaderboardCoroutines.Count <= 0) return;
            Plugin.LogInfo($"Stopping {_currentLeaderboardCoroutines.Count} leaderboard coroutines.");
            _currentLeaderboardCoroutines.ForEach(Plugin.Instance.StopCoroutine);
            _currentLeaderboardCoroutines.Clear();
        }

        public void CancelCoroutineInList(IEnumerator<UnityWebRequestAsyncOperation> c)
        {
            if (_currentLeaderboardCoroutines.Contains(c))
            {
                Plugin.Instance.StopCoroutine(c);
                _currentLeaderboardCoroutines.Remove(c);
            }
        }

        public void ShowSlider() => _slider.gameObject.SetActive(true);
        public void HideSlider() => _slider.gameObject.SetActive(false);
        public void ShowErrorText() => _errorsHolder.SetActive(true);
        public void HideErrorText() => _errorsHolder.SetActive(false);

        public static void OpenUserProfile(int id) => Application.OpenURL($"https://toottally.com/profile/{id}");
        public static void OpenLoginPage() => Application.OpenURL("https://toottally.com/login");
        public void OpenSongLeaderboard() => Application.OpenURL("https://toottally.com/song/" + _currentSelectedSongHash);
        public void OpenSongFolder()
        {
            var trackref = _savedTrackref != null ? _savedTrackref :
                           DiffCalcGlobals.selectedChart.trackRef;
            var track = TrackLookup.lookup(trackref);
            if (track == null) return;
            string path;
            if (track is CustomTrack ct)
                path = ct.folderPath;
            else
                path = $"{Application.streamingAssetsPath}/trackassets/{track.trackref}";
            if (Directory.Exists(path))
                Process.Start(path);
            else
                TootTallyNotifManager.DisplayNotif("Folder couldn't be found.");
        }

        public void ScrollToLocalScore()
        {
            if (_localScoreId == -1)
                TootTallyNotifManager.DisplayNotif("You don't have a score on that leaderboard yet");
            else if (_scoreGameObjectList.Count > 8)
            {
                _slider.value = _localScoreId / (_scoreGameObjectList.Count - 8f);
                _slider.onValueChanged.Invoke(_slider.value);
            }
        }

        private void SetTabsImages()
        {
            for (int i = 0; i < 3; i++)
            {
                GameObject currentTab = _globalLeaderboard.GetComponent<LeaderboardManager>().tabs[i];

                Button btn = currentTab.GetComponentInChildren<Button>();
                btn.image.sprite = AssetManager.GetSprite(tabsImageNames[i]);
                btn.image.color = Color.white;
                btn.colors = new ColorBlock
                {
                    normalColor = Theme.colors.leaderboard.tabs.normalColor,
                    pressedColor = Theme.colors.leaderboard.tabs.pressedColor,
                    highlightedColor = Theme.colors.leaderboard.tabs.highlightedColor,
                    fadeDuration = .1f,
                    colorMultiplier = 1f
                };
            }
            var image = GameObjectFactory.CreateClickableImageHolder(_tabs.transform, Vector2.zero, Vector2.one * 40f, AssetManager.GetSprite("folder64.png"), "Open Folder Button",
                Theme.colors.leaderboard.tabs.normalColor, Theme.colors.leaderboard.tabs.highlightedColor, OpenSongFolder);
            image.GetComponent<Image>().color = Theme.colors.leaderboard.tabs.normalColor;
            _tabs.SetActive(true);
        }

        public enum LeaderboardState
        {
            None,
            ErrorNoSongHashFound,
            ErrorNoLeaderboardFound,
            ErrorUnexpected,
            ReadyToRefresh,
            SongDataLoaded,
            SongDataMissing
        }

        private void DestroyFromParent(GameObject parent, string objectName) => GameObject.DestroyImmediate(parent.transform.Find(objectName).gameObject);
    }
}
