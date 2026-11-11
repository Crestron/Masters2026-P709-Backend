using Newtonsoft.Json;

namespace P709.Core.Nvx;

/// <summary>
/// Builds the JSON bodies POSTed to an NVX's <c>/Device</c> endpoint. Every body is a "CresNext" object rooted at <c>Device</c>
/// and holding only the properties to change: the NVX applies just what it is sent.
/// </summary>
/// <remarks>
/// <para>
/// Each body is written as a C# anonymous object with the same shape as the JSON, then serialised with Newtonsoft.Json
/// (the serialiser used throughout P709). Read them side by side: every <c>{ }</c> in the JSON is a <c>new { ... }</c>, every
/// <c>[ ]</c> is a <c>new[] { ... }</c>, and each property name is written exactly as the NVX expects it (PascalCase).
/// </para>
/// <para>
/// Never send an empty object: the NVX treats <c>{"Device":{"Preview":{}}}</c> as "delete Preview". Building every body here,
/// from a fixed shape, keeps that mistake out of the client.
/// Arrays such as <c>Streams</c> are addressed by position; these all target the first stream (index 0), the only one an NVX
/// endpoint has.
/// </para>
/// </remarks>
public static class NvxRequests
{
    /// <summary>
    /// Points the receiver at a source's multicast stream: <c>Device.StreamReceive.Streams[0].StreamLocation</c> set to
    /// <c>rtsp://&lt;multicast address&gt;</c>.
    /// </summary>
    /// <remarks>
    /// A multicast address can only be set on a transmitter; a receiver refuses <c>StreamReceive.Streams[0].MulticastAddress</c>
    /// with "Generic error". A receiver is routed through <c>StreamLocation</c> instead.
    /// </remarks>
    /// <param name="multicastAddress">The source's multicast address, for example <c>239.8.0.21</c>.</param>
    public static string SetReceiveMulticastAddress(string multicastAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(multicastAddress);

        // TODO: Lab 2.2a - Build the request that routes the receiver to a multicast address
        // Hint: The receiver is routed by its StreamLocation: an rtsp:// URL holding the multicast address. The NVX expects:
        //           {"Device":{"StreamReceive":{"Streams":[{"StreamLocation":"rtsp://239.8.0.21"}]}}}
        //       Build a C# anonymous object with exactly the same shape, then turn it into a string with
        //       JsonConvert.SerializeObject(...). Translate the JSON from the outside in:
        //           { "Name": ... }   becomes   new { Name = ... }
        //           [ { ... } ]       becomes   new[] { new { ... } }    (Streams is an array holding one stream)
        //       Build the value from the multicastAddress parameter ($"rtsp://{multicastAddress}"), not a fixed address.
        //       StopReceive, further down, is a finished example of the same shape.
        // Ref:  DM-NVX REST API, Objects > StreamReceive
        // Add your work below this line

        // Lab 2.2a - End

        return null;
    }

    /// <summary>Starts the receiver's stream: <c>Device.StreamReceive.Streams[0].Start = true</c>.</summary>
    public static string StartReceive()
    {
        // TODO: Lab 2.3a - Build the request that starts the receiver's stream
        // Hint: Start is a "trigger" property: setting it to true performs the action. The NVX expects:
        //           {"Device":{"StreamReceive":{"Streams":[{"Start":true}]}}}
        //       It is the same shape as StopReceive below, with Start instead of Stop.
        // Ref:  DM-NVX REST API, Objects > StreamReceive
        // Add your work below this line

        // Lab 2.3a - End

        return null;
    }

    /// <summary>Stops the receiver's stream: <c>Device.StreamReceive.Streams[0].Stop = true</c>.</summary>
    /// <remarks>A finished example of a request body: <c>{"Device":{"StreamReceive":{"Streams":[{"Stop":true}]}}}</c>.</remarks>
    public static string StopReceive()
    {
        var body = new
        {
            Device = new
            {
                StreamReceive = new
                {
                    Streams = new[] { new { Stop = true } }
                }
            }
        };

        return JsonConvert.SerializeObject(body);
    }

    /// <summary>Turns the preview image generator on or off: <c>Device.Preview.IsPreviewOutputEnabled</c>.</summary>
    /// <param name="isEnabled">True to produce preview images, false to stop.</param>
    public static string SetPreviewEnabled(bool isEnabled)
    {
        // TODO: Lab 2.4a - Build the request that enables or disables the preview images
        // Hint: The NVX expects:
        //           {"Device":{"Preview":{"IsPreviewOutputEnabled":true}}}
        //       Preview is not a stream, so there is no Streams array: the property sits directly inside Preview.
        //       Use the isEnabled parameter for the value.
        // Ref:  DM-NVX REST API, Objects > Preview
        // Add your work below this line

        // Lab 2.4a - End

        return null;
    }
}
