using Newtonsoft.Json.Linq;

namespace EurekaAggro.Tracker.Network;

/// <summary>
/// Encapsulates a Phoenix Channels v2 protocol wire message.
/// Wire format: [join_ref, ref, topic, event, payload]
/// </summary>
public class EurekaTrackerMessage
{
    public bool SetJoinRef { get; set; }
    public int MessageId { get; set; }
    public string Channel { get; set; }
    public string Event { get; set; }
    public JToken Payload { get; set; }

    public EurekaTrackerMessage(bool setJoinRef, int messageId, string channel, string @event, JToken payload)
    {
        SetJoinRef = setJoinRef;
        MessageId = messageId;
        Channel = channel;
        Event = @event;
        Payload = payload;
    }

    public string ToMessage()
    {
        JArray array = new()
        {
            SetJoinRef ? "1" : null,
            MessageId.ToString(),
            Channel,
            Event,
            Payload ?? new JObject(),
        };

        return array.ToString(Newtonsoft.Json.Formatting.None);
    }
}
