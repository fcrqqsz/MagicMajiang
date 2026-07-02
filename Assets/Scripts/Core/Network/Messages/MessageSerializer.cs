using UnityEngine;
using System;

namespace MahjongGame.Core.Network.Messages
{
    public static class MessageSerializer
    {
        public static string Serialize<T>(string type, int seq, T payload)
        {
            string dataJson = payload != null ? JsonUtility.ToJson(payload) : "{}";
            var envelope = new NetworkMessageEnvelope
            {
                type = type,
                seq = seq,
                data = dataJson
            };
            return JsonUtility.ToJson(envelope);
        }

        public static NetworkMessageEnvelope DeserializeEnvelope(string json)
        {
            try
            {
                return JsonUtility.FromJson<NetworkMessageEnvelope>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MessageSerializer] Failed to deserialize envelope: {ex.Message}");
                return null;
            }
        }

        public static T DeserializePayload<T>(string dataJson)
        {
            try
            {
                return JsonUtility.FromJson<T>(dataJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MessageSerializer] Failed to deserialize payload to {typeof(T).Name}: {ex.Message}");
                return default;
            }
        }
    }
}
