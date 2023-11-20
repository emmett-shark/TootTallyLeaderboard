﻿using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace TootTallyLeaderboard
{
    public static class BetterScrollSpeedSliderPatcher
    {
        private const string CONFIG_NAME = "BetterScrollSpeed.cfg";
        private const string CONFIG_FIELD = "SliderValues";
        private const uint DEFAULT_MAX = 250;
        private const uint DEFAULT_MIN = 5;
        private const float DEFAULT_VALUE = 100;

        public static void PatchScrollSpeedSlider()
        {
            string configPath = Path.Combine(Paths.BepInExRootPath, "config/");
            ConfigFile config = new ConfigFile(configPath + CONFIG_NAME, true);
            Options option = new Options()
            {
                Max = config.Bind(CONFIG_FIELD, nameof(option.Max), DEFAULT_MAX),
                Min = config.Bind(CONFIG_FIELD, nameof(option.Min), DEFAULT_MIN),
                LastValue = config.Bind(CONFIG_FIELD, nameof(option.LastValue), DEFAULT_VALUE)
            };
            if (option.Min.Value >= option.Max.Value)
                Plugin.LogError("Slider MAX has to be greater than Slider MIN");
            else if (option.Min.Value <= 4)
                Plugin.LogError("Slider MIN has to be greater or equal to 5");
            else if (option.Max.Value >= 1000)
                Plugin.LogError("Buddy. What are you trying to do?? You're never gonna play with 1k+ scrollspeed...");

            if (option.Max.Value >= option.Min.Value || option.Min.Value <= 4 || option.Max.Value >= 1000)
            {
                option.Min.Value = DEFAULT_MIN;
                option.Max.Value = DEFAULT_MAX;
            }

            if (option.LastValue.Value < option.Min.Value || option.LastValue.Value > option.Max.Value) //Don't even try...
                option.LastValue.Value = DEFAULT_VALUE;

            Text yoinkText = GameObject.Find("MainCanvas/FullScreenPanel/ScrollSpeed-lbl").GetComponent<Text>();
            Transform handleTransform = GameObject.Find("MainCanvas/FullScreenPanel/Slider/Handle Slide Area/Handle").transform;

            Transform starTransform = GameObject.Find("MainCanvas/FullScreenPanel/difficulty stars/star1").transform;
            Text scrollSpeedSliderText = GameObject.Instantiate(yoinkText, handleTransform);

            scrollSpeedSliderText.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            scrollSpeedSliderText.alignment = TextAnchor.MiddleCenter;
            scrollSpeedSliderText.fontSize = 12;
            scrollSpeedSliderText.text = ((int)(GlobalVariables.gamescrollspeed * 100)).ToString();

            Slider slider = GameObject.Find("MainCanvas/FullScreenPanel/Slider").GetComponent<Slider>();
            slider.minValue = option.Min.Value / 100f;
            slider.maxValue = option.Max.Value / 100f;
            slider.value = option.LastValue.Value / 100f;
            scrollSpeedSliderText.text = SliderValueToText(slider.value);
            slider.onValueChanged.AddListener((float _value) => { option.LastValue.Value = _value * 100f; scrollSpeedSliderText.text = SliderValueToText(_value); });

            GameObject.Find("MainCanvas/FullScreenPanel/ScrollSpeed-lbl").gameObject.SetActive(false);

        }

        public static string SliderValueToText(float value)
        {
            //return Mathf.RoundToInt(value * 100).ToString();

            if (value >= 1)
                return Mathf.FloorToInt(value * 100).ToString();
            else
                return Mathf.CeilToInt(value * 100).ToString();
        }
    }
    public class Options
    {
        public ConfigEntry<uint> Max { get; set; }
        public ConfigEntry<uint> Min { get; set; }
        public ConfigEntry<float> LastValue { get; set; }

    }
}
