using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using BaboonAPI.Hooks.Tracks;
using BepInEx;
using Newtonsoft.Json;
using TootTallyAccounts;
using TootTallyCore;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyGlobals;
using TootTallyCore.Utils.TootTallyNotifs;
using TootTallyGameModifiers;
using TrombLoader.CustomTracks;
using TrombLoader.Data;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace TootTallyLeaderboard.Replays
{
    public class NewReplaySystem
    {
        public static List<string> incompatibleReplayVersions = new List<string> { "1.0.0" };
        public const string REPLAY_VERSION = "2.1.3";

        private int _frameIndex, _tootIndex;

        private ReplayData _replayData;
        private dynamic[] _lastFrame, _currentFrame;
        private dynamic[] _currentToot;
        private dynamic[] _currentNote;

        public float GetReplaySpeed { get => _replayData.gamespeedmultiplier; }

        private bool _wasTouchScreenUsed;
        private bool _wasTabletUsed = false;
        private int _maxCombo;
        private bool _isLastNote;
        public bool _isTooting;
        public Vector3 _mousePos;
        public bool GetIsTooting { get => _isTooting; }

        public string GetUsername { get => _replayData.username; }
        public string GetSongName { get => _replayData.song; }

        public bool IsFullCombo { get => GlobalVariables.gameplay_notescores[0] == 0 && GlobalVariables.gameplay_notescores[1] == 0 && GlobalVariables.gameplay_notescores[2] == 0; }
        public bool IsTripleS { get => GlobalVariables.gameplay_notescores[0] == 0 && GlobalVariables.gameplay_notescores[1] == 0 && GlobalVariables.gameplay_notescores[2] == 0 && GlobalVariables.gameplay_notescores[3] == 0; }

        public bool GetIsOldReplay => TootTallyGlobalVariables.isOldReplay;
        public BackgroundPuppetController _backgroundPuppetController;
        public int ScreenWidth { get => _replayData.screenwidth; }
        public int ScreenHeight { get => _replayData.screenheight; }

        public NewReplaySystem()
        {
            _replayData = new ReplayData(120);
        }

        #region ReplayRecorder

        public void SetupRecording(int targetFramerate)
        {
            _replayData = new ReplayData(targetFramerate);
            _replayData.gamemodifiers = GameModifierManager.GetModifiersString();
            _replayData.scrollspeed = GlobalVariables.gamescrollspeed;
            _replayData.gamespeedmultiplier = float.Parse(ReplaySystemManager.gameSpeedMultiplier.ToString("0.00"));
            var track = TrackLookup.lookup(GlobalVariables.chosen_track_data.trackref);
            _replayData.song = track.trackname_long;
            if (track is CustomTrack)
                _replayData.songhash = SongDataHelper.GetSongHash(track);
            else
                _replayData.songhash = track.trackref;

            _currentFrame = new dynamic[5];
            _currentNote = new dynamic[9];
            _currentToot = new dynamic[3];
            _replayData.screenwidth = Screen.width;
            _replayData.screenheight = Screen.height;
            Plugin.LogInfo("Started recording replay");
        }

        public void SetReplayDefaultNoteLength(int spacing)
        {
            _replayData.defaultnotelength = spacing;
        }

        public int GetDefaultNoteLength() => _replayData.defaultnotelength;

        public string GetVersion => _replayData.version;

        public void SetStartTime()
        {
            _replayData.SetStartTime();
            Plugin.LogInfo($"Replay started recording at {_replayData.starttime}");
        }

        public void SetEndTime()
        {
            _replayData.SetEndTime();
            Plugin.LogInfo($"Replay recording finished at {_replayData.endtime}");
        }

        public void SetUsernameAndSongName(string username, string songname)
        {
            _replayData.username = username;
            _replayData.song = songname;
        }


        public void RecordFrameData(GameController __instance, float time, float noteHolderPosition)
        {
            if (Input.touchCount > 0) _wasTouchScreenUsed = true;

            var pointerPos = Round(__instance.pointer.transform.localPosition.y);
            var noteHolderPos = Round(noteHolderPosition, 10f);
            var mousePosY = (int)Input.mousePosition.y;

            if (_lastFrame == null || (noteHolderPos != _lastFrame[2] && pointerPos != _lastFrame[1] && mousePosY != _lastFrame[4]))
            {
                _replayData.framedata.Add(new dynamic[5]
                {
                    Round(time, 10f),
                    noteHolderPos,
                    pointerPos,
                    (int)Input.mousePosition.x,
                    mousePosY,
                });
                _lastFrame = _replayData.framedata.Last();
            }


        }

        private const float FLOAT_PRECISION = 1000f;
        public float Round(float f, float mult = 1f) => Mathf.Round(f * FLOAT_PRECISION * mult) / (FLOAT_PRECISION * mult);

        public void RecordNoteDataPrefix(GameController __instance)
        {
            _currentNote[0] = Round(__instance.notescoreaverage);
            _currentNote[1] = __instance.released_button_between_notes ? 1 : 0;
        }

        public void RecordNoteDataPostfix(GameController __instance)
        {
            _currentNote[2] = __instance.currentnoteindex;
            _currentNote[3] = __instance.highestcombocounter;
            _currentNote[4] = __instance.multiplier;
            _currentNote[5] = __instance.totalscore;
            _currentNote[6] = Round(__instance.currenthealth);
            _currentNote[7] = _maxCombo = __instance.highestcombo_level;
            _currentNote[8] = _lastTally;
            _replayData.notedata.Add(new dynamic[9]
            {
                _currentNote[0],
                _currentNote[1],
                _currentNote[2],
                _currentNote[3],
                _currentNote[4],
                _currentNote[5],
                _currentNote[6],
                _currentNote[7],
                _currentNote[8],
            });
        }

        private int _lastTally;

        public void SaveLastNoteTally(int tallyIndex) => _lastTally = tallyIndex;

        public void RecordToot(float time, float noteHolderPosition, bool isTooting)
        {
            var keyPresses = new List<int>();
            for (int i = 0; i < GlobalVariables.keyboard_key_keycodes.Length; i++)
                if (Input.GetKey(GlobalVariables.keyboard_key_keycodes[i]))
                    keyPresses.Add(GlobalVariables.keyboard_key_keycodes[i].GetHashCode());
            if (Input.GetMouseButton(0))
                keyPresses.Add(-1);
            if (Input.GetMouseButton(1))
                keyPresses.Add(-2);

            _replayData.tootdata.Add(new dynamic[4]
            {
                Round(time, 10f),
                Round(noteHolderPosition, 10f),
                isTooting ? 1 : 0,
                keyPresses
            });
        }

        public string GetRecordedReplayJson(string uuid)
        {
            _replayData.uuid = uuid;
            _replayData.input = GetInputTypeString();
            _replayData.finalscore = GlobalVariables.gameplay_scoretotal;
            _replayData.maxcombo = _maxCombo;
            //That's absolutely disgusting I love it.
            _replayData.finalnotetallies = new int[]
            {
                _replayData.notedata.Count(x => x[(int)NDStruct.TL] == 4),
                _replayData.notedata.Count(x => x[(int)NDStruct.TL] == 3),
                _replayData.notedata.Count(x => x[(int)NDStruct.TL] == 2),
                _replayData.notedata.Count(x => x[(int)NDStruct.TL] == 1),
                _replayData.notedata.Count(x => x[(int)NDStruct.TL] == 0),
            };

            return JsonConvert.SerializeObject(_replayData);
        }
        private string GetInputTypeString()
        {
            if (_wasTabletUsed)
                return "Tablet";
            else if (_wasTouchScreenUsed)
                return "Touch";
            return "Mouse";
        }
        #endregion

        #region ReplayPlayer

        public void OnReplayPlayerStart()
        {
            _frameIndex = 0;
            _tootIndex = 0;
            _isTooting = false;
            _isLastNote = false;
            _currentFrame = _replayData.framedata.Count > 0 ? _replayData.framedata.First() : null;
            _currentNote = _replayData.notedata.Count > 0 ? _replayData.notedata.First() : null;
            _currentToot = new dynamic[] { 0, 0, 0, new List<int>() };
        }

        public void OnReplayRewind(float newTiming, GameController __instance)
        {
            if (_replayData == null) return;

            if (_replayData.framedata.Count != 0)
            {
                _frameIndex = Mathf.Clamp(_replayData.framedata.FindIndex(frame => (float)frame[(int)FDStruct.T] > newTiming) - 1, 0, _replayData.framedata.Count - 1);
                _currentFrame = _replayData.framedata[_frameIndex];
            }
            if (_replayData.tootdata.Count != 0)
            {
                _tootIndex = Mathf.Clamp(_replayData.tootdata.FindIndex(frame => (float)frame[(int)TDStruct.T] > newTiming) - 1, 0, _replayData.tootdata.Count - 1);
                _currentToot = _replayData.tootdata[_tootIndex];
            }

            _isTooting = false;

            if (_replayData.notedata.Count != 0 && __instance.currentnoteindex != 0)
            {
                var allPrevNotes = _replayData.notedata.Where(note => (int)note[(int)NDStruct.I] <= __instance.currentnoteindex - 1);
                if (allPrevNotes.Count() > 0)
                {
                    __instance.currentscore = (int)allPrevNotes.Last()[(int)NDStruct.S];
                    var allNoteTally = allPrevNotes.Select(note => note[(int)NDStruct.TL]);
                    __instance.scores_A = allNoteTally.Count(tally => (int)tally == 4);
                    __instance.scores_B = allNoteTally.Count(tally => (int)tally == 3);
                    __instance.scores_C = allNoteTally.Count(tally => (int)tally == 2);
                    __instance.scores_D = allNoteTally.Count(tally => (int)tally == 1);
                    __instance.scores_F = allNoteTally.Count(tally => (int)tally == 0);
                }
                else
                {
                    __instance.currentscore = 0;
                    __instance.scores_A = __instance.scores_B = __instance.scores_C = __instance.scores_D = __instance.scores_F = 0;
                }


            }
        }

        public ReplayState LoadReplay(string replayFileName)
        {
            string replayDir = Path.Combine(Paths.BepInExRootPath, "Replays/");
            if (!Directory.Exists(replayDir))
            {
                Plugin.LogInfo("Replay folder not found");
                return ReplayState.ReplayLoadError;
            }
            if (!File.Exists(replayDir + replayFileName + ".ttr"))
            {
                Plugin.LogInfo("Replay File does not exist");
                return ReplayState.ReplayLoadNotFound;
            }

            string jsonFileFromZip = FileHelper.ReadJsonFromFile(replayDir, replayFileName + ".ttr");

            var replayVersion = JsonConvert.DeserializeObject<ReplayVersion>(jsonFileFromZip).version;
            _replayData = JsonConvert.DeserializeObject<ReplayData>(jsonFileFromZip);
            if (_replayData.screenwidth == 0 || _replayData.screenheight == 0)
            {
                _replayData.screenwidth = 1920;
                _replayData.screenheight = 1080;
            }

            TootTallyGlobalVariables.isOldReplay = IsOldReplayFormat(replayVersion);
            if (GetIsOldReplay)
            {
                ConvertToCurrentReplayVersion(ref _replayData);
                GlobalVariables.gamescrollspeed = _replayData.scrollspeed;
            }

            if (incompatibleReplayVersions.Contains(_replayData.pluginbuilddate.ToString()))
            {
                TootTallyNotifManager.DisplayNotif($"Replay incompatible:\nReplay Build Date is {_replayData.pluginbuilddate}\nCurrent Build Date is {TootTallyCore.Plugin.BUILDDATE}");
                Plugin.LogError("Cannot load replay:");
                Plugin.LogError("   Replay Build Date is " + _replayData.pluginbuilddate);
                Plugin.LogError("   Current Plugin Build Date " + TootTallyCore.Plugin.BUILDDATE);
                return ReplayState.ReplayLoadErrorIncompatible;
            }

            GameModifierManager.LoadModifiersFromString(_replayData.gamemodifiers ?? "");

            return ReplayState.ReplayLoadSuccess;
        }

        private bool IsOldReplayFormat(string version) => version == null || string.Compare(version, "2.0.0") < 0;

        private void ConvertToCurrentReplayVersion(ref ReplayData replayData)
        {
            TootTallyNotifManager.DisplayNotif("Converting old replay format...");

            dynamic[][] frameData = new dynamic[replayData.framedata.Count][];
            for (int i = 0; i < replayData.framedata.Count; i++)
            {
                frameData[i] = new dynamic[5]
                {
                    (int)Math.Abs(replayData.framedata[i][0]), //Replace time with noteholder
                    (int)replayData.framedata[i][0],
                    (float)replayData.framedata[i][1] / 100f, //Pointer position
                    0,
                    0
                };
            }

            dynamic[][] noteData = new dynamic[replayData.notedata.Count][];
            for (int i = 0; i < replayData.notedata.Count; i++)
            {
                noteData[i] = new dynamic[9]
                {
                    (float)replayData.notedata[i][5] / 1000f, //NoteScore
                    -1,
                    (int)replayData.notedata[i][0], //Index
                    -1,
                    (int)replayData.notedata[i][2], //Multiplier
                    (int)replayData.notedata[i][1], //TotalScore
                    (int)replayData.notedata[i][3], //Health
                    -1,
                    (int)replayData.notedata[i][4] //Note Tally
                };
            }

            dynamic[][] tootData = new dynamic[replayData.tootdata.Count][];
            for (int i = 0; i < replayData.tootdata.Count; i++)
            {
                tootData[i] = new dynamic[3]
                {
                    (int)Math.Abs(replayData.tootdata[i][0]), //Replace time with noteholder
                    replayData.tootdata[i][0],
                    i % 2 == 0 ? 1 : 0,
                };

            }


            replayData.framedata = frameData.ToList();
            replayData.notedata = noteData.ToList();
            replayData.tootdata = tootData.ToList();
        }



        public void PlaybackReplay(GameController __instance, float time)
        {
            if (Plugin.Instance.option.ShowcaseMode.Value)
                Cursor.visible = false;
            else
                Cursor.visible = true;
            __instance.previous_high_score_surpassed = true; // prevents the new high score yellow highlight from appearing
            if (!__instance.controllermode) __instance.controllermode = true; //Still required to not make the mouse position update

            if (GetIsOldReplay)
                time = (_replayData.pluginbuilddate < 20230705 ?
                 Math.Abs(__instance.noteholder.transform.position.x) : Math.Abs(__instance.noteholderr.anchoredPosition.x)) * GetNoteHolderPrecisionMultiplier();
            else
                time -= ((GlobalVariables.localsettings.latencyadjust / 1000f) - _replayData.audiolatency / 1000f);
            PlaybackTimeFrameData(time);
            PlaybackTimeTootData(time);
            if (_replayData.framedata.Count > _frameIndex && _lastFrame != null && _currentFrame != null)
                InterpolateTimeCursorPosition(time, __instance);



        }

        public static float GetNoteHolderPrecisionMultiplier() => 10 / (GlobalVariables.gamescrollspeed <= 1 ? GlobalVariables.gamescrollspeed : 1);

        private void InterpolateTimeCursorPosition(float time, GameController __instance)
        {
            if (_currentFrame[(int)FDStruct.T] - _lastFrame[(int)FDStruct.T] > 0)
            {
                var by = (time - (float)_lastFrame[(int)FDStruct.T]) / ((float)_currentFrame[(int)FDStruct.T] - (float)_lastFrame[(int)FDStruct.T]);
                var newCursorPosition = EasingHelper.Lerp((float)_lastFrame[(int)FDStruct.P], (float)_currentFrame[(int)FDStruct.P], by);
                var newMousePositionX = EasingHelper.Lerp((float)_lastFrame[(int)FDStruct.MX], (float)_currentFrame[(int)FDStruct.MX], by);
                var newMousePositionY = EasingHelper.Lerp((float)_lastFrame[(int)FDStruct.MY], (float)_currentFrame[(int)FDStruct.MY], by);

                SetCursorPosition(__instance, newCursorPosition);
                _mousePos = new(newMousePositionX, newMousePositionY, 0f);
                __instance.puppet_humanc.doPuppetControl(-newCursorPosition / 225); //225 is half of the Gameplay area:450
                if (_backgroundPuppetController)
                {
                    _backgroundPuppetController.DoPuppetControl(-newCursorPosition / 225, __instance.vibratoamt);
                }
            }
            else
                SetCursorPosition(__instance, (float)_currentFrame[(int)FDStruct.P]);
            _mousePos = new((float)_currentFrame[(int)FDStruct.MX], (float)_currentFrame[(int)FDStruct.MY], 0f);
        }

        private void PlaybackTimeFrameData(float time)
        {

            if (_lastFrame != _currentFrame && _currentFrame != null && time >= _currentFrame[(int)FDStruct.T])
                _lastFrame = _currentFrame;

            if (_replayData.framedata.Count > _frameIndex && (_currentFrame == null || time >= _currentFrame[(int)FDStruct.T]))
            {
                _frameIndex = _replayData.framedata.FindIndex(_frameIndex > 1 ? _frameIndex - 1 : 0, x => time < x[(int)FDStruct.T]);
                if (_replayData.framedata.Count > _frameIndex && _frameIndex != -1)
                    _currentFrame = _replayData.framedata[_frameIndex];
            }
        }

        private void PlaybackTimeTootData(float time)
        {
            if (_currentToot != null && time >= _currentToot[(int)TDStruct.T] && _isTooting != (_currentToot[(int)TDStruct.O] == 1))
                _isTooting = _currentToot[(int)TDStruct.O] == 1;

            if (_replayData.tootdata.Count > _tootIndex && time >= _currentToot[(int)TDStruct.T])
                _currentToot = _replayData.tootdata[_tootIndex++];
        }

        public void SetNoteScorePrefix(GameController __instance)
        {
            if (_replayData.notedata.Count != 0 && !_isLastNote)
            {
                _currentNote = _replayData.notedata.Find(x => x[(int)NDStruct.I] == __instance.currentnoteindex);
                _isLastNote = _replayData.notedata.Last()[(int)NDStruct.I] == __instance.currentnoteindex;
            }
            if (_currentNote != null)
            {
                __instance.notescoreaverage = (float)_currentNote[(int)NDStruct.NS];
                if (!GetIsOldReplay)
                    __instance.released_button_between_notes = _currentNote[(int)NDStruct.R] == 1;

            }
        }

        public void SetNoteScorePostFix(GameController __instance)
        {
            if (_currentNote != null)
            {
                __instance.rainbowcontroller.champmode = _currentNote[(int)NDStruct.H] == 100;
                __instance.multiplier = (int)_currentNote[(int)NDStruct.M];
                if (__instance.currentscore < 0)
                    __instance.currentscore = (int)_currentNote[(int)NDStruct.S];
                __instance.totalscore = (int)_currentNote[(int)NDStruct.S];
                __instance.currenthealth = (float)_currentNote[(int)NDStruct.H];
                if (!GetIsOldReplay)
                {
                    __instance.highestcombocounter = (int)_currentNote[(int)NDStruct.C];
                    __instance.highestcombo_level = (int)_currentNote[(int)NDStruct.HC];
                }
                _currentNote = null;
            }
        }

        #endregion

        #region Utils
        public void SetCursorPosition(GameController __instance, float newPosition)
        {
            Vector2 pointerPosition = __instance.pointer.transform.localPosition;
            pointerPosition.y = newPosition;
            __instance.pointer.transform.localPosition = pointerPosition;
        }
        public void ClearData()
        {
            Plugin.LogInfo("Replay data cleared");
            _replayData?.ClearData();
        }

        [Serializable]
        private class ReplayVersion
        {
            public string version { get; set; }
        }

        [Serializable]
        public class ReplayData
        {
            public string version { get; set; }
            public string username { get; set; }
            public string starttime { get; set; }
            public string endtime { get; set; }
            public string input { get; set; }
            public string song { get; set; }
            public string uuid { get; set; }
            public float samplerate { get; set; }
            public float scrollspeed { get; set; }
            public int defaultnotelength { get; set; }
            public float gamespeedmultiplier { get; set; }
            public string gamemodifiers { get; set; }
            public float audiolatency { get; set; }
            public int pluginbuilddate { get; set; }
            public string gameversion { get; set; }
            public string songhash { get; set; }
            public int finalscore { get; set; }
            public int maxcombo { get; set; }
            public int[] finalnotetallies { get; set; }
            public List<dynamic[]> framedata { get; set; }
            public List<dynamic[]> notedata { get; set; }
            public List<dynamic[]> tootdata { get; set; }
            public int screenwidth { get; set; }
            public int screenheight { get; set; }

            public ReplayData(float sampleRate)
            {
                framedata = new List<dynamic[]>(524288);
                notedata = new List<dynamic[]>(524288);
                tootdata = new List<dynamic[]>(524288);
                pluginbuilddate = TootTallyCore.Plugin.BUILDDATE;
                version = REPLAY_VERSION;
                username = TootTallyUser.userInfo.username;
                audiolatency = GlobalVariables.localsettings.latencyadjust;
                gameversion = Application.version;
                samplerate = sampleRate;
                screenwidth = 0;
                screenheight = 0;
            }

            public void ClearData()
            {
                framedata?.Clear();
                notedata?.Clear();
                tootdata?.Clear();
            }

            public void SetStartTime()
            {
                starttime = new DateTimeOffset(DateTime.Now.ToUniversalTime()).ToUnixTimeSeconds().ToString();
            }

            public void SetEndTime()
            {
                endtime = new DateTimeOffset(DateTime.Now.ToUniversalTime()).ToUnixTimeSeconds().ToString();
            }

        }

        public enum FDStruct
        {
            T, //Time
            N, //NoteHolder
            P, //CursorPosition
            MX, //MouseX
            MY //MouseY
        }

        public enum TDStruct
        {
            T, //Time
            N, //NoteHolder
            O //IsTooting
        }

        public enum NDStruct
        {
            NS, //NoteScoreAverage
            R, //ReleasedBetweenNotes
            I, //Index
            C, //Combo
            M, //Multiplier
            S, //Score
            H, //Health
            HC, //HighestCombo
            TL, //TallyScore
        }

        public enum ReplayState
        {
            None,
            ReplayLoadErrorIncompatible,
            ReplayLoadError,
            ReplayLoadSuccess,
            ReplayLoadNotFound,
        }

        #endregion
    }
}
