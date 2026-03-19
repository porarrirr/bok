#include "core_webrtc/session_controller.h"

#ifndef P2PAUDIO_HAS_LIBDATACHANNEL
#define P2PAUDIO_HAS_LIBDATACHANNEL 0
#endif

#include <chrono>
#include <array>
#include <cctype>
#include <functional>
#include <iomanip>
#include <limits>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string_view>

#if P2PAUDIO_HAS_LIBDATACHANNEL
extern "C" {
#include <rtc/rtc.h>
}
#endif

namespace core_webrtc {

namespace {

constexpr auto kNegotiationTimeout = std::chrono::seconds(12);
constexpr auto kDataChannelOpenTimeout = std::chrono::seconds(12);
constexpr int kMaxBufferedAmountBytes = 256000;

std::string trim_copy(std::string value) {
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front())) != 0) {
        value.erase(value.begin());
    }
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back())) != 0) {
        value.pop_back();
    }
    return value;
}

std::string format_error(const std::string& label, int code) {
    std::ostringstream oss;
    oss << label << "_" << code;
    return oss.str();
}

std::optional<std::string> extract_candidate_address(const std::string& candidate_line) {
    std::string normalized = trim_copy(candidate_line);
    if (normalized.rfind("a=", 0) == 0) {
        normalized = normalized.substr(2);
    }
    std::istringstream iss(normalized);
    std::string part;
    std::vector<std::string> parts;
    while (iss >> part) {
        parts.push_back(part);
    }
    if (parts.size() < 6) {
        return std::nullopt;
    }
    return parts[4];
}

std::string candidate_type(const std::string& candidate_line) {
    const std::string marker = " typ ";
    const auto marker_index = candidate_line.find(marker);
    if (marker_index == std::string::npos) {
        return "";
    }
    const auto type_start = marker_index + marker.size();
    const auto type_end = candidate_line.find(' ', type_start);
    if (type_end == std::string::npos) {
        return trim_copy(candidate_line.substr(type_start));
    }
    return trim_copy(candidate_line.substr(type_start, type_end - type_start));
}

std::optional<int> parse_octet(std::string_view segment) {
    if (segment.empty() || segment.size() > 3) {
        return std::nullopt;
    }
    int value = 0;
    for (char c : segment) {
        if (c < '0' || c > '9') {
            return std::nullopt;
        }
        value = (value * 10) + (c - '0');
    }
    if (value < 0 || value > 255) {
        return std::nullopt;
    }
    return value;
}

std::optional<std::array<int, 4>> parse_ipv4(const std::string& address) {
    std::array<int, 4> octets{};
    std::size_t start = 0;
    for (std::size_t i = 0; i < 4; ++i) {
        const auto dot = address.find('.', start);
        std::string_view segment;
        if (i == 3) {
            segment = std::string_view(address).substr(start);
        } else if (dot != std::string::npos) {
            segment = std::string_view(address).substr(start, dot - start);
            start = dot + 1;
        } else {
            return std::nullopt;
        }
        const auto parsed = parse_octet(segment);
        if (!parsed.has_value()) {
            return std::nullopt;
        }
        octets[i] = parsed.value();
    }
    return octets;
}

std::string classify_path_from_candidate_address(const std::string& address) {
    const auto ipv4 = parse_ipv4(address);
    if (!ipv4.has_value()) {
        return "unknown";
    }

    const auto a = ipv4.value()[0];
    const auto b = ipv4.value()[1];
    const auto c = ipv4.value()[2];

    if ((a == 192 && b == 168 && c == 42) || (a == 172 && b == 20 && c == 10)) {
        return "usb_tether";
    }
    if (a == 10) {
        return "wifi_lan";
    }
    if (a == 172 && b >= 16 && b <= 31) {
        return "wifi_lan";
    }
    if (a == 192 && b == 168) {
        return "wifi_lan";
    }
    return "unknown";
}

std::string fingerprint_from_sdp(const std::string& sdp) {
    const std::string prefix = "a=fingerprint:";
    std::size_t start = 0;
    while (start < sdp.size()) {
        const auto end = sdp.find('\n', start);
        std::string line = sdp.substr(start, end == std::string::npos ? std::string::npos : end - start);
        if (!line.empty() && line.back() == '\r') {
            line.pop_back();
        }
        if (line.rfind(prefix, 0) == 0) {
            return trim_copy(line.substr(prefix.size()));
        }
        if (end == std::string::npos) {
            break;
        }
        start = end + 1;
    }
    return "";
}

#if P2PAUDIO_HAS_LIBDATACHANNEL

session_controller* controller_from(void* user_ptr) {
    return static_cast<session_controller*>(user_ptr);
}

void on_local_description_cb(int pc, const char* sdp, const char* type, void* user_ptr) {
    auto* controller = controller_from(user_ptr);
    if (controller == nullptr) {
        return;
    }
    controller->handle_local_description_callback(pc, sdp, type);
}

void on_local_candidate_cb(int pc, const char* candidate, const char* mid, void* user_ptr) {
    (void)mid;
    auto* controller = controller_from(user_ptr);
    if (controller == nullptr || candidate == nullptr) {
        return;
    }
    controller->handle_local_candidate_callback(pc, candidate);
}

void on_state_change_cb(int pc, rtcState state, void* user_ptr) {
    auto* controller = controller_from(user_ptr);
    if (controller == nullptr) {
        return;
    }
    controller->handle_state_change_callback(pc, static_cast<int>(state));
}

void on_gathering_state_cb(int pc, rtcGatheringState state, void* user_ptr) {
    auto* controller = controller_from(user_ptr);
    if (controller == nullptr) {
        return;
    }
    controller->handle_gathering_state_callback(pc, static_cast<int>(state));
}

void on_data_channel_cb(int pc, int dc, void* user_ptr) {
    auto* controller = controller_from(user_ptr);
    if (controller == nullptr) {
        return;
    }
    controller->handle_data_channel_callback(pc, dc);
}

#endif

}  // namespace

session_controller::session_controller() {
    diagnostics_.path_type = "unknown";
    diagnostics_.local_candidates_count = 0;
    diagnostics_.selected_candidate_pair_type = "";
    diagnostics_.failure_hint = "";
}

offer_result session_controller::create_offer() {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    std::unique_lock<std::mutex> guard(mutex_);
    close_locked();

    rtcConfiguration configuration{};
    configuration.disableAutoNegotiation = true;

    peer_connection_id_ = rtcCreatePeerConnection(&configuration);
    if (peer_connection_id_ < 0) {
        throw std::runtime_error(format_error("create_peer_connection_failed", peer_connection_id_));
    }

    rtcSetUserPointer(peer_connection_id_, this);
    if (rtcSetLocalDescriptionCallback(peer_connection_id_, on_local_description_cb) < 0 ||
        rtcSetLocalCandidateCallback(peer_connection_id_, on_local_candidate_cb) < 0 ||
        rtcSetStateChangeCallback(peer_connection_id_, on_state_change_cb) < 0 ||
        rtcSetGatheringStateChangeCallback(peer_connection_id_, on_gathering_state_cb) < 0 ||
        rtcSetDataChannelCallback(peer_connection_id_, on_data_channel_cb) < 0) {
        close_locked();
        throw std::runtime_error("register_peer_callbacks_failed");
    }

    offer_result result;
    session_id_ = create_session_id();
    is_offerer_ = true;
    answer_applied_ = false;
    session_open_ = true;
    local_description_ready_ = false;
    gathering_complete_ = false;
    data_channel_open_ = false;
    diagnostics_.path_type = "unknown";
    diagnostics_.local_candidates_count = 0;
    diagnostics_.selected_candidate_pair_type.clear();
    diagnostics_.failure_hint.clear();

    rtcDataChannelInit data_channel_init{};
    data_channel_init.reliability.unordered = true;
    data_channel_init.reliability.unreliable = true;
    data_channel_init.reliability.maxRetransmits = 0;
    data_channel_id_ = rtcCreateDataChannelEx(peer_connection_id_, "audio-pcm", &data_channel_init);
    if (data_channel_id_ < 0) {
        close_locked();
        throw std::runtime_error(format_error("create_data_channel_failed", data_channel_id_));
    }
    configure_data_channel_locked(data_channel_id_);
    condition_.notify_all();

    const auto set_description = rtcSetLocalDescription(peer_connection_id_, "offer");
    if (set_description < 0) {
        close_locked();
        throw std::runtime_error(format_error("set_local_description_offer_failed", set_description));
    }

    if (!condition_.wait_for(guard, kNegotiationTimeout, [this] {
            return local_description_ready_ && local_description_type_ == "offer";
        })) {
        diagnostics_.failure_hint = "local_offer_timeout";
        close_locked();
        throw std::runtime_error("local_offer_timeout");
    }
    if (!condition_.wait_for(guard, kNegotiationTimeout, [this] { return gathering_complete_; })) {
        diagnostics_.failure_hint = "ice_gathering_timeout";
        close_locked();
        throw std::runtime_error("ice_gathering_timeout");
    }

    std::array<char, 16384> local_description{};
    const auto offer_size = rtcGetLocalDescription(
        peer_connection_id_,
        local_description.data(),
        static_cast<int>(local_description.size())
    );
    if (offer_size <= 0) {
        close_locked();
        throw std::runtime_error("get_local_offer_failed");
    }
    last_offer_sdp_.assign(local_description.data(), static_cast<std::size_t>(offer_size));
    last_offer_sdp_ = trim_copy(last_offer_sdp_);
    if (!last_offer_sdp_.empty() && last_offer_sdp_.back() == '\0') {
        last_offer_sdp_.pop_back();
    }

    result.session_id = session_id_;
    result.offer_sdp = last_offer_sdp_;
    result.fingerprint = fingerprint_from_sdp(last_offer_sdp_);
    if (result.fingerprint.empty()) {
        result.fingerprint = fake_fingerprint(last_offer_sdp_);
    }
    result.info = diagnostics_;
    return result;
#else
    std::lock_guard<std::mutex> guard(mutex_);
    offer_result result;
    session_id_ = create_session_id();
    last_offer_sdp_ = "v=0\r\ns=p2paudio-offer\r\na=setup:actpass\r\n";
    result.session_id = session_id_;
    result.offer_sdp = last_offer_sdp_;
    result.fingerprint = fake_fingerprint(session_id_);

    diagnostics_.path_type = "unknown";
    diagnostics_.local_candidates_count = 0;
    diagnostics_.selected_candidate_pair_type.clear();
    diagnostics_.failure_hint = "libdatachannel_disabled";
    session_open_ = true;
    answer_applied_ = false;
    result.info = diagnostics_;
    return result;
#endif
}

answer_result session_controller::create_answer(const std::string& offer_sdp) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    if (offer_sdp.empty()) {
        throw std::runtime_error("offer_sdp_empty");
    }

    std::unique_lock<std::mutex> guard(mutex_);
    close_locked();

    rtcConfiguration configuration{};
    configuration.disableAutoNegotiation = true;

    peer_connection_id_ = rtcCreatePeerConnection(&configuration);
    if (peer_connection_id_ < 0) {
        throw std::runtime_error(format_error("create_peer_connection_failed", peer_connection_id_));
    }

    rtcSetUserPointer(peer_connection_id_, this);
    if (rtcSetLocalDescriptionCallback(peer_connection_id_, on_local_description_cb) < 0 ||
        rtcSetLocalCandidateCallback(peer_connection_id_, on_local_candidate_cb) < 0 ||
        rtcSetStateChangeCallback(peer_connection_id_, on_state_change_cb) < 0 ||
        rtcSetGatheringStateChangeCallback(peer_connection_id_, on_gathering_state_cb) < 0 ||
        rtcSetDataChannelCallback(peer_connection_id_, on_data_channel_cb) < 0) {
        close_locked();
        throw std::runtime_error("register_peer_callbacks_failed");
    }

    answer_result result;
    session_id_ = create_session_id();
    is_offerer_ = false;
    answer_applied_ = false;
    session_open_ = true;
    local_description_ready_ = false;
    gathering_complete_ = false;
    data_channel_open_ = false;
    diagnostics_.path_type = "unknown";
    diagnostics_.local_candidates_count = 0;
    diagnostics_.selected_candidate_pair_type.clear();
    diagnostics_.failure_hint.clear();

    const auto set_remote = rtcSetRemoteDescription(peer_connection_id_, offer_sdp.c_str(), "offer");
    if (set_remote < 0) {
        close_locked();
        throw std::runtime_error(format_error("set_remote_offer_failed", set_remote));
    }

    const auto set_local = rtcSetLocalDescription(peer_connection_id_, "answer");
    if (set_local < 0) {
        close_locked();
        throw std::runtime_error(format_error("set_local_answer_failed", set_local));
    }

    if (!condition_.wait_for(guard, kNegotiationTimeout, [this] {
            return local_description_ready_ && local_description_type_ == "answer";
        })) {
        diagnostics_.failure_hint = "local_answer_timeout";
        close_locked();
        throw std::runtime_error("local_answer_timeout");
    }
    if (!condition_.wait_for(guard, kNegotiationTimeout, [this] { return gathering_complete_; })) {
        diagnostics_.failure_hint = "ice_gathering_timeout";
        close_locked();
        throw std::runtime_error("ice_gathering_timeout");
    }

    std::array<char, 16384> local_description{};
    const auto answer_size = rtcGetLocalDescription(
        peer_connection_id_,
        local_description.data(),
        static_cast<int>(local_description.size())
    );
    if (answer_size <= 0) {
        close_locked();
        throw std::runtime_error("get_local_answer_failed");
    }
    last_answer_sdp_.assign(local_description.data(), static_cast<std::size_t>(answer_size));
    last_answer_sdp_ = trim_copy(last_answer_sdp_);
    if (!last_answer_sdp_.empty() && last_answer_sdp_.back() == '\0') {
        last_answer_sdp_.pop_back();
    }

    result.answer_sdp = last_answer_sdp_;
    result.fingerprint = fingerprint_from_sdp(last_answer_sdp_);
    if (result.fingerprint.empty()) {
        result.fingerprint = fake_fingerprint(last_answer_sdp_);
    }
    result.info = diagnostics_;
    return result;
#else
    std::lock_guard<std::mutex> guard(mutex_);
    (void)offer_sdp;
    answer_result result;
    if (!session_open_) {
        session_id_ = create_session_id();
    }

    last_answer_sdp_ = "v=0\r\ns=p2paudio-answer\r\na=setup:active\r\n";
    result.answer_sdp = last_answer_sdp_;
    result.fingerprint = fake_fingerprint(session_id_);

    diagnostics_.path_type = "unknown";
    diagnostics_.local_candidates_count = 0;
    diagnostics_.selected_candidate_pair_type.clear();
    diagnostics_.failure_hint = "libdatachannel_disabled";
    session_open_ = true;
    answer_applied_ = false;
    result.info = diagnostics_;
    return result;
#endif
}

bool session_controller::apply_answer(const std::string& answer_sdp) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    if (answer_sdp.empty()) {
        std::lock_guard<std::mutex> guard(mutex_);
        diagnostics_.failure_hint = "answer_sdp_empty";
        return false;
    }

    std::unique_lock<std::mutex> guard(mutex_);
    if (!session_open_ || peer_connection_id_ < 0 || !is_offerer_) {
        diagnostics_.failure_hint = "offer_session_not_ready";
        return false;
    }

    const auto set_remote = rtcSetRemoteDescription(peer_connection_id_, answer_sdp.c_str(), "answer");
    if (set_remote < 0) {
        diagnostics_.failure_hint = format_error("set_remote_answer_failed", set_remote);
        return false;
    }

    answer_applied_ = true;
    diagnostics_.failure_hint.clear();

    if (!condition_.wait_for(guard, kDataChannelOpenTimeout, [this] {
            return data_channel_open_ || (data_channel_id_ >= 0 && rtcIsOpen(data_channel_id_) != 0);
        })) {
        diagnostics_.failure_hint = "data_channel_open_timeout";
        return false;
    }

    if (data_channel_id_ >= 0 && rtcIsOpen(data_channel_id_) != 0) {
        data_channel_open_ = true;
    }
    diagnostics_.failure_hint.clear();
    return true;
#else
    std::lock_guard<std::mutex> guard(mutex_);
    (void)answer_sdp;
    if (!session_open_) {
        diagnostics_.failure_hint = "session_not_open";
        return false;
    }

    diagnostics_.failure_hint = "libdatachannel_disabled";
    return false;
#endif
}

bool session_controller::send_pcm_frame(const uint8_t* data, std::size_t size) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    if (data == nullptr || size == 0 || size > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
        std::lock_guard<std::mutex> guard(mutex_);
        diagnostics_.failure_hint = "invalid_pcm_packet";
        return false;
    }

    std::lock_guard<std::mutex> guard(mutex_);
    if (!session_open_ || !answer_applied_) {
        diagnostics_.failure_hint = "session_not_connected";
        return false;
    }
    if (data_channel_id_ < 0) {
        diagnostics_.failure_hint = "data_channel_not_available";
        return false;
    }
    if (!data_channel_open_ && rtcIsOpen(data_channel_id_) == 0) {
        diagnostics_.failure_hint = "data_channel_not_open";
        return false;
    }
    data_channel_open_ = true;

    const auto buffered_amount = rtcGetBufferedAmount(data_channel_id_);
    if (buffered_amount > kMaxBufferedAmountBytes) {
        diagnostics_.failure_hint = "send_pcm_backpressure";
        return false;
    }

    const auto send_result = rtcSendMessage(data_channel_id_, reinterpret_cast<const char*>(data), static_cast<int>(size));
    if (send_result < 0) {
        diagnostics_.failure_hint = format_error("send_pcm_failed", send_result);
        return false;
    }
    return true;
#else
    std::lock_guard<std::mutex> guard(mutex_);
    (void)data;
    (void)size;
    diagnostics_.failure_hint = "libdatachannel_disabled";
    return false;
#endif
}

bool session_controller::try_pop_received_pcm(std::vector<uint8_t>& packet) {
    std::lock_guard<std::mutex> guard(mutex_);
    if (received_packets_.empty()) {
        return false;
    }
    packet = std::move(received_packets_.front());
    received_packets_.pop_front();
    return true;
}

void session_controller::close() {
    std::lock_guard<std::mutex> guard(mutex_);
    close_locked();
}

diagnostics session_controller::get_diagnostics() const {
    std::lock_guard<std::mutex> guard(mutex_);
    return diagnostics_;
}

std::string session_controller::create_session_id() const {
    const auto now = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    std::ostringstream oss;
    oss << "win-" << std::hex << now;
    return oss.str();
}

std::string session_controller::fake_fingerprint(const std::string& source) const {
    std::hash<std::string> hasher;
    const auto value = hasher(source);
    std::ostringstream oss;
    oss << "sha-256 " << std::hex << std::setw(16) << std::setfill('0') << value;
    return oss.str();
}

void session_controller::handle_local_description_callback(int pc, const char* sdp, const char* type) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    std::lock_guard<std::mutex> guard(mutex_);
    if (pc != peer_connection_id_) {
        return;
    }
    local_description_ready_ = sdp != nullptr;
    local_description_type_ = type == nullptr ? "" : type;
    if (sdp != nullptr) {
        if (local_description_type_ == "offer") {
            last_offer_sdp_ = sdp;
        } else if (local_description_type_ == "answer") {
            last_answer_sdp_ = sdp;
        }
    }
    condition_.notify_all();
#else
    (void)pc;
    (void)sdp;
    (void)type;
#endif
}

void session_controller::handle_local_candidate_callback(int pc, const char* candidate) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    std::lock_guard<std::mutex> guard(mutex_);
    if (pc != peer_connection_id_ || candidate == nullptr) {
        return;
    }

    const std::string candidate_line = candidate;
    if (candidate_line.find(" typ host") != std::string::npos) {
        diagnostics_.local_candidates_count += 1;
        const auto address = extract_candidate_address(candidate_line);
        if (address.has_value()) {
            diagnostics_.path_type = classify_path_from_candidate_address(address.value());
        }
    }
#else
    (void)pc;
    (void)candidate;
#endif
}

void session_controller::refresh_selected_pair_locked() {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    if (peer_connection_id_ < 0) {
        return;
    }

    std::array<char, 1024> local_candidate{};
    std::array<char, 1024> remote_candidate{};
    const auto pair_result = rtcGetSelectedCandidatePair(
        peer_connection_id_,
        local_candidate.data(),
        static_cast<int>(local_candidate.size()),
        remote_candidate.data(),
        static_cast<int>(remote_candidate.size())
    );
    if (pair_result < 0) {
        return;
    }

    const std::string local = local_candidate.data();
    const std::string remote = remote_candidate.data();
    const auto local_type = candidate_type(local);
    const auto remote_type = candidate_type(remote);
    if (!local_type.empty() && !remote_type.empty()) {
        diagnostics_.selected_candidate_pair_type = local_type + "-" + remote_type;
    }

    const auto address = extract_candidate_address(local);
    if (address.has_value()) {
        diagnostics_.path_type = classify_path_from_candidate_address(address.value());
    }
#endif
}

void session_controller::handle_state_change_callback(int pc, int state) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    std::lock_guard<std::mutex> guard(mutex_);
    if (pc != peer_connection_id_) {
        return;
    }

    if (state == RTC_CONNECTED) {
        diagnostics_.failure_hint.clear();
        refresh_selected_pair_locked();
    } else if (state == RTC_FAILED) {
        diagnostics_.failure_hint = "ice_failed";
    } else if (state == RTC_DISCONNECTED) {
        diagnostics_.failure_hint = "ice_disconnected";
    } else if (state == RTC_CLOSED) {
        data_channel_open_ = false;
    }
    condition_.notify_all();
#else
    (void)pc;
    (void)state;
#endif
}

void session_controller::handle_gathering_state_callback(int pc, int state) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    std::lock_guard<std::mutex> guard(mutex_);
    if (pc != peer_connection_id_) {
        return;
    }

    if (state == RTC_GATHERING_COMPLETE) {
        gathering_complete_ = true;
        condition_.notify_all();
    }
#else
    (void)pc;
    (void)state;
#endif
}

void session_controller::configure_data_channel_locked(int dc) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    data_channel_id_ = dc;
    rtcSetUserPointer(dc, this);
    rtcSetOpenCallback(dc, [](int channel, void* ptr) {
        auto* current = controller_from(ptr);
        if (current == nullptr) {
            return;
        }
        std::lock_guard<std::mutex> lock(current->mutex_);
        if (channel == current->data_channel_id_) {
            current->data_channel_open_ = true;
            current->diagnostics_.failure_hint.clear();
            current->condition_.notify_all();
        }
    });
    rtcSetClosedCallback(dc, [](int channel, void* ptr) {
        auto* current = controller_from(ptr);
        if (current == nullptr) {
            return;
        }
        std::lock_guard<std::mutex> lock(current->mutex_);
        if (channel == current->data_channel_id_) {
            current->data_channel_open_ = false;
            current->condition_.notify_all();
        }
    });
    rtcSetErrorCallback(dc, [](int channel, const char* error, void* ptr) {
        auto* current = controller_from(ptr);
        if (current == nullptr) {
            return;
        }
        std::lock_guard<std::mutex> lock(current->mutex_);
        if (channel == current->data_channel_id_) {
            current->diagnostics_.failure_hint = error == nullptr ? "data_channel_error" : error;
            current->condition_.notify_all();
        }
    });
    rtcSetMessageCallback(dc, [](int channel, const char* message, int size, void* ptr) {
        auto* current = controller_from(ptr);
        if (current == nullptr || message == nullptr || size <= 0) {
            return;
        }
        std::lock_guard<std::mutex> lock(current->mutex_);
        if (channel != current->data_channel_id_) {
            return;
        }
        if (current->received_packets_.size() >= 256) {
            current->received_packets_.pop_front();
        }
        current->received_packets_.emplace_back(
            reinterpret_cast<const uint8_t*>(message),
            reinterpret_cast<const uint8_t*>(message) + size
        );
    });

    if (rtcIsOpen(dc) != 0) {
        data_channel_open_ = true;
    }
#else
    (void)dc;
#endif
}

void session_controller::handle_data_channel_callback(int pc, int dc) {
#if P2PAUDIO_HAS_LIBDATACHANNEL
    std::lock_guard<std::mutex> guard(mutex_);
    if (pc != peer_connection_id_) {
        return;
    }
    configure_data_channel_locked(dc);
    condition_.notify_all();
#else
    (void)pc;
    (void)dc;
#endif
}

void session_controller::close_locked() {
    condition_.notify_all();

#if P2PAUDIO_HAS_LIBDATACHANNEL
    if (data_channel_id_ >= 0) {
        rtcClose(data_channel_id_);
        rtcDeleteDataChannel(data_channel_id_);
        data_channel_id_ = -1;
    }
    if (peer_connection_id_ >= 0) {
        rtcClosePeerConnection(peer_connection_id_);
        rtcDeletePeerConnection(peer_connection_id_);
        peer_connection_id_ = -1;
    }
#endif

    session_open_ = false;
    answer_applied_ = false;
    local_description_ready_ = false;
    gathering_complete_ = false;
    data_channel_open_ = false;
    is_offerer_ = false;
    session_id_.clear();
    last_offer_sdp_.clear();
    last_answer_sdp_.clear();
    local_description_type_.clear();
    received_packets_.clear();
    diagnostics_.local_candidates_count = 0;
    diagnostics_.selected_candidate_pair_type.clear();
    diagnostics_.failure_hint.clear();
    if (diagnostics_.path_type.empty()) {
        diagnostics_.path_type = "unknown";
    }
}

}  // namespace core_webrtc
