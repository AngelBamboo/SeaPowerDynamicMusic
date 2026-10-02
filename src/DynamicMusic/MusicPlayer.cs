using System;
using System.Collections;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 音乐播放器。使用两个 AudioSource 做交叉淡化，切歌时不会突然断掉。
    /// 不依赖游戏原有的 MusicManager，因此游戏更新时不容易失效。
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        private AudioSource _sourceA;
        private AudioSource _sourceB;
        private AudioSource _active;

        private MusicTrack _currentTrack;
        private Coroutine _fadeRoutine;

        private float _targetVolume = 0.8f;
        private bool _muted;

        /// <summary>当前正在播放的曲目，没有则为 null。</summary>
        public MusicTrack CurrentTrack
        {
            get { return _currentTrack; }
        }

        public bool IsPlaying
        {
            get { return _active != null && _active.isPlaying; }
        }

        /// <summary>当前曲目是否已播放结束（用于自动接下一首）。</summary>
        public bool CurrentFinished
        {
            get
            {
                if (_currentTrack == null || _active == null) return false;
                if (!_active.isPlaying && _active.time <= 0f) return true;
                return false;
            }
        }

        public static MusicPlayer Create(Transform parent)
        {
            var go = new GameObject("SeaPowerDynamicMusic_Player");
            if (parent != null) go.transform.SetParent(parent, false);
            DontDestroyOnLoad(go);
            return go.AddComponent<MusicPlayer>();
        }

        private void Awake()
        {
            _sourceA = CreateSource("ChannelA");
            _sourceB = CreateSource("ChannelB");
            _active = _sourceA;
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;   // 纯 2D，不受听者位置影响
            src.volume = 0f;
            src.priority = 0;        // 音乐优先级最高，不被其它音效挤掉
            src.bypassEffects = true;
            src.bypassListenerEffects = true;
            return src;
        }

        /// <summary>把音量作用到两个音源（只影响正在播放的那一路）。</summary>
        public void SetVolume(float volume01)
        {
            _targetVolume = Mathf.Clamp01(volume01);
            if (_fadeRoutine == null && _active != null && _active.isPlaying)
            {
                _active.volume = _muted ? 0f : _targetVolume;
            }
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            SetVolume(_targetVolume);
        }

        /// <summary>
        /// 交叉淡化到指定曲目。fade 为 0 时直接硬切。
        /// </summary>
        public void CrossfadeTo(MusicTrack track, float fade)
        {
            if (track == null || track.Clip == null)
            {
                Plugin.Verbose("CrossfadeTo: 曲目为空或尚未加载，忽略");
                return;
            }

            if (_currentTrack == track && _active != null && _active.isPlaying)
            {
                return;
            }

            AudioSource incoming = (_active == _sourceA) ? _sourceB : _sourceA;
            AudioSource outgoing = _active;

            incoming.clip = track.Clip;
            incoming.time = 0f;
            incoming.volume = 0f;
            incoming.Play();

            _currentTrack = track;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            // 上一次淡出没走完的通道直接归零，避免残留声音
            AudioSource stale = (incoming == _sourceA) ? _sourceB : _sourceA;
            if (stale != outgoing && stale.isPlaying)
            {
                stale.Stop();
                stale.volume = 0f;
            }

            if (_muted) return;

            if (fade <= 0.01f)
            {
                if (outgoing != null && outgoing.isPlaying) outgoing.Stop();
                incoming.volume = _targetVolume;
                _active = incoming;
            }
            else
            {
                _fadeRoutine = StartCoroutine(CrossfadeRoutine(outgoing, incoming, fade));
                _active = incoming;
            }

            Plugin.LogInfo(string.Format("播放: {0} [{1}]", track.DisplayName,
                track.PrimaryScene));
        }

        /// <summary>淡出并停止。fade 为 0 时立即停止。</summary>
        public void Stop(float fade)
        {
            _currentTrack = null;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (fade <= 0.01f)
            {
                _sourceA.Stop();
                _sourceB.Stop();
                _sourceA.volume = 0f;
                _sourceB.volume = 0f;
                return;
            }

            _fadeRoutine = StartCoroutine(StopRoutine(fade));
        }

        private IEnumerator CrossfadeRoutine(AudioSource outgoing, AudioSource incoming, float duration)
        {
            float t = 0f;
            float outStart = outgoing != null ? outgoing.volume : 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // 用 smoothstep 让听感上的过渡更自然
                k = k * k * (3f - 2f * k);

                incoming.volume = _targetVolume * k;
                if (outgoing != null) outgoing.volume = outStart * (1f - k);

                yield return null;
            }

            incoming.volume = _targetVolume;
            if (outgoing != null)
            {
                outgoing.Stop();
                outgoing.volume = 0f;
            }
            _fadeRoutine = null;
        }

        private IEnumerator StopRoutine(float duration)
        {
            float a0 = _sourceA.volume;
            float b0 = _sourceB.volume;
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(t / duration);
                _sourceA.volume = a0 * k;
                _sourceB.volume = b0 * k;
                yield return null;
            }

            _sourceA.Stop();
            _sourceB.Stop();
            _sourceA.volume = 0f;
            _sourceB.volume = 0f;
            _fadeRoutine = null;
        }

        /// <summary>临时压低音量，用于任务结算等需要安静下来的场合。</summary>
        public void Duck(float factor, float seconds)
        {
            StartCoroutine(DuckRoutine(factor, seconds));
        }

        private IEnumerator DuckRoutine(float factor, float seconds)
        {
            float original = _targetVolume;
            SetVolume(original * Mathf.Clamp01(factor));
            yield return new WaitForSecondsRealtime(seconds);
            SetVolume(original);
        }
    }
}
