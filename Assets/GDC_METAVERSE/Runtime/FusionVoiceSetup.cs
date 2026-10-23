#if GDC_HAS_PHOTON_VOICE
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
#endif
using UnityEngine;

namespace GDC.Metaverse
{
    /// <summary>
    /// One-call Photon Voice for Fusion setup on a NetworkRunner, created from code (no scene objects).
    /// Call it right after adding the NetworkRunner and before StartGame. Player prefabs need a VoiceNetworkObject
    /// (GDC_METAVERSE > Voice > Add Voice To Selected Network Prefab). Requires the Voice App ID in Fusion's
    /// PhotonAppSettings and the com.photonengine.voice-fusion package; otherwise it does nothing and returns false.
    /// </summary>
    public static class FusionVoiceSetup
    {
        /// <param name="runnerObject">The GameObject holding the NetworkRunner.</param>
        /// <param name="transmit">This player's microphone is sent to others.</param>
        /// <param name="playVoices">This player hears others (false mutes every received voice locally).</param>
        /// <param name="spatial">3D voices placed at each player's object.</param>
        public static bool Configure(GameObject runnerObject, bool transmit, bool playVoices, bool spatial = true)
        {
#if GDC_HAS_PHOTON_VOICE
            if (string.IsNullOrWhiteSpace(Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdVoice))
            {
                Debug.LogWarning("[GDC_METAVERSE] No Photon Voice App ID (Fusion PhotonAppSettings > App Id Voice): voice is off.");
                return false;
            }
            var recorder = runnerObject.AddComponent<Recorder>();
            recorder.RecordWhenJoined = transmit;
            recorder.TransmitEnabled = transmit;
            var voice = runnerObject.AddComponent<FusionVoiceClient>();
            voice.PrimaryRecorder = recorder;
            // Template under an inactive holder: the template never runs, its copies (one per remote player) are active.
            var holder = new GameObject("Voice Speaker Template Holder");
            holder.SetActive(false);
            holder.transform.SetParent(runnerObject.transform, false);
            var template = new GameObject("Voice Speaker");
            template.transform.SetParent(holder.transform, false);
            var source = template.AddComponent<AudioSource>();
            source.spatialBlend = spatial ? 1f : 0f;
            source.mute = !playVoices;
            template.AddComponent<Speaker>();
            voice.SpeakerPrefab = template;
            return true;
#else
            Debug.LogWarning("[GDC_METAVERSE] Photon Voice for Fusion (com.photonengine.voice-fusion) is not installed: voice is off.");
            return false;
#endif
        }
    }
}
