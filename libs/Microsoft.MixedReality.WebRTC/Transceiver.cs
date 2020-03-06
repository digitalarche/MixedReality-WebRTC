// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using Microsoft.MixedReality.WebRTC.Interop;

namespace Microsoft.MixedReality.WebRTC
{
    /// <summary>
    /// Type of media track or media transceiver.
    /// </summary>
    /// <remarks>
    /// This is the projection of <c>mrsMediaKind</c> from the interop API.
    /// </remarks>
    public enum MediaKind : uint
    {
        /// <summary>
        /// Audio data.
        /// </summary>
        Audio = 0,

        /// <summary>
        /// Video data.
        /// </summary>
        Video = 1
    }

    /// <summary>
    /// Transceiver of a peer connection.
    /// 
    /// A transceiver is a media "pipe" connecting the local and remote peers, and used to transmit media
    /// data (audio or video) between the peers. The transceiver has a media flow direction indicated whether
    /// it is sending and/or receiving any media, or is inactive. When sending some media, the transceiver
    /// local track is used as the source of the media. Conversely, when receiving some media, that media is
    /// delivered to the remote media track of the transceiver. As a convenience, both tracks can be null to
    /// avoid sending or ignore the received media, although this does not influence the media flow direction.
    /// </summary>
    /// <remarks>
    /// This object corresponds roughly to the same-named notion in the WebRTC standard when using the
    /// Unified Plan SDP semantic.
    /// For Plan B, where RTP transceivers are not available, this wrapper tries to emulate the Unified Plan
    /// transceiver concept, and is therefore providing an abstraction over the WebRTC concept of transceivers.
    /// 
    /// Note that this object is not disposable because the lifetime of the native transceiver is tied to the
    /// lifetime of the peer connection (cannot be removed), and therefore the two collections
    /// <see cref="PeerConnection.AudioTransceivers"/> and <see cref="PeerConnection.VideoTransceivers"/> own
    /// their objects and should continue to contain the list of wrappers for the native transceivers.
    /// </remarks>
    /// <seealso cref="AudioTransceiver"/>
    /// <seealso cref="VideoTransceiver"/>
    public abstract class Transceiver
    {
        /// <summary>
        /// Direction of the media flowing inside the transceiver.
        /// </summary>
        public enum Direction : int
        {
            /// <summary>
            /// Transceiver is both sending to and receiving from the remote peer connection.
            /// </summary>
            SendReceive = 0,

            /// <summary>
            /// Transceiver is sending to the remote peer, but is not receiving any media from the remote peer.
            /// </summary>
            SendOnly = 1,

            /// <summary>
            /// Transceiver is receiving from the remote peer, but is not sending any media to the remote peer.
            /// </summary>
            ReceiveOnly = 2,

            /// <summary>
            /// Transceiver is inactive, neither sending nor receiving any media data.
            /// </summary>
            Inactive = 3,
        }

        /// <summary>
        /// A name for the transceiver, used for logging and debugging.
        /// </summary>
        public string Name { get; } = string.Empty;

        /// <summary>
        /// Type of media carried by the transceiver, and by extension type of media of its tracks.
        /// </summary>
        public MediaKind MediaKind { get; }

        /// <summary>
        /// Peer connection this transceiver is part of.
        /// </summary>
        public PeerConnection PeerConnection { get; } = null;

        /// <summary>
        /// Index of the media line in the SDP protocol for this transceiver. This also corresponds
        /// to the index of the transceiver inside <see cref="PeerConnection.Transceivers"/>.
        /// </summary>
        public int MlineIndex { get; } = -1;

        /// <summary>
        /// Transceiver direction desired by the user.
        /// If a negotiation is pending, then this is the next direction that will be negotiated when
        /// calling <see cref="PeerConnection.CreateOffer"/> or <see cref="PeerConnection.CreateAnswer"/>.
        /// Otherwise this is equal to <see cref="NegotiatedDirection"/>.
        /// </summary>
        /// <seealso cref="SetDirection(Direction)"/>
        public Direction DesiredDirection
        {
            get { return _desiredDirection; }
            set { SetDirection(value); }
        }

        /// <summary>
        /// Last negotiated transceiver direction. This is equal to <see cref="DesiredDirection"/>
        /// after a negotiation is completed, but remains constant until the next SDP negotiation
        /// when changing the desired direction with <see cref="SetDirection(Direction)"/>.
        /// </summary>
        /// <seealso cref="DesiredDirection"/>
        /// <seealso cref="SetDirection(Direction)"/>
        public Direction? NegotiatedDirection { get; protected set; } = null;

        /// <summary>
        /// Backing field for <see cref="DesiredDirection"/>.
        /// Default is Send+Receive, as it is in implementation.
        /// </summary>
        protected Direction _desiredDirection = Direction.SendReceive;

        /// <summary>
        /// Handle to the native Transceiver object.
        /// </summary>
        /// <remarks>
        /// In native land this is a <code>Microsoft::MixedReality::WebRTC::TransceiverHandle</code>.
        /// </remarks>
        internal TransceiverHandle _nativeHandle = new TransceiverHandle();

        private IntPtr _argsRef = IntPtr.Zero;

        /// <summary>
        /// Create a new transceiver associated with a given peer connection.
        /// </summary>
        /// <param name="mediaKind">The media kind of the transceiver and its tracks.</param>
        /// <param name="peerConnection">The peer connection owning this transceiver.</param>
        /// <param name="mlineIndex">The transceiver media line index in SDP.</param>
        /// <param name="name">The transceiver name.</param>
        protected Transceiver(MediaKind mediaKind, PeerConnection peerConnection, int mlineIndex, string name)
        {
            MediaKind = mediaKind;
            PeerConnection = peerConnection;
            MlineIndex = mlineIndex;
            Name = name;
        }

        internal void SetHandle(TransceiverHandle handle)
        {
            Debug.Assert(!handle.IsClosed);
            // Either first-time assign or no-op (assign same value again)
            Debug.Assert(_nativeHandle.IsInvalid || (_nativeHandle == handle));
            if (_nativeHandle != handle)
            {
                _nativeHandle = handle;
                TransceiverInterop.RegisterCallbacks(this, out _argsRef);
            }
        }

        /// <summary>
        /// Change the media flowing direction of the transceiver.
        /// This triggers a renegotiation needed event to synchronize with the remote peer.
        /// </summary>
        /// <param name="newDirection">The new flowing direction.</param>
        /// <seealso cref="DesiredDirection"/>
        /// <seealso cref="NegotiatedDirection"/>
        public void SetDirection(Direction newDirection)
        {
            if (newDirection == _desiredDirection)
            {
                return;
            }
            var res = TransceiverInterop.Transceiver_SetDirection(_nativeHandle, newDirection);
            Utils.ThrowOnErrorCode(res);
            _desiredDirection = newDirection;
        }

        /// <summary>
        /// Callback on local track muted due to transceiver direction change.
        /// </summary>
        /// <param name="muted"><c>true</c> if the track is muted, or <c>false</c> otherwise.</param>
        protected abstract void OnLocalTrackMuteChanged(bool muted);

        /// <summary>
        /// Callback on remote track muted due to transceiver direction change.
        /// </summary>
        /// <param name="muted"><c>true</c> if the track is muted, or <c>false</c> otherwise.</param>
        protected abstract void OnRemoteTrackMuteChanged(bool muted);

        /// <summary>
        /// Callback on internal implementation state changed to synchronize the cached state of this wrapper.
        /// </summary>
        /// <param name="negotiatedDirection">Current negotiated direction of the transceiver</param>
        /// <param name="desiredDirection">Current desired direction of the transceiver</param>
        internal void OnStateUpdated(Direction? negotiatedDirection, Direction desiredDirection)
        {
            _desiredDirection = desiredDirection;

            if (negotiatedDirection != NegotiatedDirection)
            {
                bool hadSendBefore = HasSend(NegotiatedDirection);
                bool hasSendNow = HasSend(negotiatedDirection);
                bool hadRecvBefore = HasRecv(NegotiatedDirection);
                bool hasRecvNow = HasRecv(negotiatedDirection);

                NegotiatedDirection = negotiatedDirection;

                if (hadSendBefore != hasSendNow)
                {
                    OnLocalTrackMuteChanged(!hasSendNow);
                }
                if (hadRecvBefore != hasRecvNow)
                {
                    OnRemoteTrackMuteChanged(!hasRecvNow);
                }
            }
        }

        /// <summary>
        /// Check whether the given direction includes sending.
        /// </summary>
        /// <param name="dir">The direction to check.</param>
        /// <returns><c>true</c> if direction is <see cref="Direction.SendOnly"/> or <see cref="Direction.SendReceive"/>.</returns>
        public static bool HasSend(Direction dir)
        {
            return (dir == Direction.SendOnly) || (dir == Direction.SendReceive);
        }

        /// <summary>
        /// Check whether the given direction includes receiving.
        /// </summary>
        /// <param name="dir">The direction to check.</param>
        /// <returns><c>true</c> if direction is <see cref="Direction.ReceiveOnly"/> or <see cref="Direction.SendReceive"/>.</returns>
        public static bool HasRecv(Direction dir)
        {
            return (dir == Direction.ReceiveOnly) || (dir == Direction.SendReceive);
        }

        /// <summary>
        /// Check whether the given direction includes sending.
        /// </summary>
        /// <param name="dir">The direction to check.</param>
        /// <returns><c>true</c> if direction is <see cref="Direction.SendOnly"/> or <see cref="Direction.SendReceive"/>.</returns>
        public static bool HasSend(Direction? dir)
        {
            return dir.HasValue && ((dir == Direction.SendOnly) || (dir == Direction.SendReceive));
        }

        /// <summary>
        /// Check whether the given direction includes receiving.
        /// </summary>
        /// <param name="dir">The direction to check.</param>
        /// <returns><c>true</c> if direction is <see cref="Direction.ReceiveOnly"/> or <see cref="Direction.SendReceive"/>.</returns>
        public static bool HasRecv(Direction? dir)
        {
            return dir.HasValue && ((dir == Direction.ReceiveOnly) || (dir == Direction.SendReceive));
        }
    }
}