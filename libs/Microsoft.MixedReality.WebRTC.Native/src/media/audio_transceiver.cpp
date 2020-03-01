// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "pch.h"

#include "audio_transceiver.h"
#include "interop/global_factory.h"
#include "peer_connection.h"

namespace Microsoft::MixedReality::WebRTC {

AudioTransceiver::AudioTransceiver(
    RefPtr<GlobalFactory> global_factory,
    PeerConnection& owner,
    int mline_index,
    std::string name,
    const AudioTransceiverInitConfig& config) noexcept
    : Transceiver(std::move(global_factory),
                  MediaKind::kAudio,
                  owner,
                  config.desired_direction),
      mline_index_(mline_index),
      name_(std::move(name)),
      interop_handle_(config.transceiver_interop_handle) {}

AudioTransceiver::AudioTransceiver(
    RefPtr<GlobalFactory> global_factory,
    PeerConnection& owner,
    int mline_index,
    std::string name,
    rtc::scoped_refptr<webrtc::RtpTransceiverInterface> transceiver,
    const AudioTransceiverInitConfig& config) noexcept
    : Transceiver(std::move(global_factory),
                  MediaKind::kAudio,
                  owner,
                  transceiver,
                  config.desired_direction),
      mline_index_(mline_index),
      name_(std::move(name)),
      interop_handle_(config.transceiver_interop_handle) {}

AudioTransceiver::~AudioTransceiver() {
  // Be sure to clean-up WebRTC objects before unregistering ourself, which
  // could lead to the GlobalFactory being destroyed and the WebRTC threads
  // stopped.
  transceiver_ = nullptr;
}

Result AudioTransceiver::SetDirection(Direction new_direction) noexcept {
  if (transceiver_) {  // Unified Plan
    if (new_direction == desired_direction_) {
      return Result::kSuccess;
    }
    transceiver_->SetDirection(ToRtp(new_direction));
  } else {  // Plan B
    // nothing to do
  }
  desired_direction_ = new_direction;
  FireStateUpdatedEvent(mrsTransceiverStateUpdatedReason::kSetDirection);
  return Result::kSuccess;
}

Result AudioTransceiver::SetLocalTrack(
    RefPtr<LocalAudioTrack> local_track) noexcept {
  if (local_track_ == local_track) {
    return Result::kSuccess;
  }
  Result result = Result::kSuccess;
  webrtc::MediaStreamTrackInterface* const new_track =
      local_track ? local_track->impl() : nullptr;
  rtc::scoped_refptr<webrtc::RtpSenderInterface> rtp_sender;
  if (IsUnifiedPlan()) {
    rtp_sender = transceiver_->sender();
    if (!rtp_sender->SetTrack(new_track)) {
      result = Result::kInvalidOperation;
    }
  }
  else {
    RTC_DCHECK(IsPlanB());
    SetTrackPlanB(new_track);
  }
  if (result != Result::kSuccess) {
    if (local_track) {
      RTC_LOG(LS_ERROR) << "Failed to set local audio track "
                        << local_track->GetName() << " of audio transceiver "
                        << GetName() << ".";
    } else {
      RTC_LOG(LS_ERROR)
          << "Failed to clear local audio track from audio transceiver "
          << GetName() << ".";
    }
    return result;
  }
  if (local_track_) {
    // Detach old local track
    // Keep a pointer to the track because |local_track_| gets NULL'd. No need
    // to keep a reference, because |owner_| has one.
    LocalAudioTrack* const track = local_track_.get();
    track->OnRemovedFromPeerConnection(*owner_, this, rtp_sender);
    owner_->OnLocalTrackRemovedFromAudioTransceiver(*this, *track);
  }
  local_track_ = std::move(local_track);
  if (local_track_) {
    // Attach new local track
    local_track_->OnAddedToPeerConnection(*owner_, this, rtp_sender);
    owner_->OnLocalTrackAddedToAudioTransceiver(*this, *local_track_);
  }
  return Result::kSuccess;
}

void AudioTransceiver::OnLocalTrackAdded(RefPtr<LocalAudioTrack> track) {
  // this may be called multiple times with the same track
  RTC_DCHECK(!local_track_ || (local_track_ == track));
  local_track_ = std::move(track);
}

void AudioTransceiver::OnRemoteTrackAdded(RefPtr<RemoteAudioTrack> track) {
  // this may be called multiple times with the same track
  RTC_DCHECK(!remote_track_ || (remote_track_ == track));
  remote_track_ = std::move(track);
}

void AudioTransceiver::OnLocalTrackRemoved(LocalAudioTrack* track) {
  RTC_DCHECK_EQ(track, local_track_.get());
  local_track_ = nullptr;
}

void AudioTransceiver::OnRemoteTrackRemoved(RemoteAudioTrack* track) {
  RTC_DCHECK_EQ(track, remote_track_.get());
  remote_track_ = nullptr;
}

}  // namespace Microsoft::MixedReality::WebRTC