#pragma once

#include <cstdint>
#include <cstddef>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace core_webrtc {

struct diagnostics {
    std::string path_type;
    int local_candidates_count = 0;
    std::string selected_candidate_pair_type;
    std::string failure_hint;
};

struct offer_result {
    std::string session_id;
    std::string offer_sdp;
    std::string fingerprint;
    diagnostics info;
};

struct answer_result {
    std::string answer_sdp;
    std::string fingerprint;
    diagnostics info;
};

class session_controller {
public:
    session_controller();
    ~session_controller() = default;

    offer_result create_offer();
    answer_result create_answer(const std::string& offer_sdp);
    bool apply_answer(const std::string& answer_sdp);
    bool send_pcm_frame(const uint8_t* data, std::size_t size);
    bool try_pop_received_pcm(std::vector<uint8_t>& packet);
    void close();
    diagnostics get_diagnostics() const;
    void handle_local_description_callback(int pc, const char* sdp, const char* type);
    void handle_local_candidate_callback(int pc, const char* candidate);
    void handle_state_change_callback(int pc, int state);
    void handle_gathering_state_callback(int pc, int state);
    void handle_data_channel_callback(int pc, int dc);

private:
    std::string create_session_id() const;
    std::string fake_fingerprint(const std::string& source) const;
    void refresh_selected_pair_locked();
    void configure_data_channel_locked(int dc);
    void close_locked();
    
    mutable std::mutex mutex_;
    std::condition_variable condition_;
    diagnostics diagnostics_;
    bool session_open_ = false;
    bool answer_applied_ = false;
    bool local_description_ready_ = false;
    bool gathering_complete_ = false;
    bool data_channel_open_ = false;
    int peer_connection_id_ = -1;
    int data_channel_id_ = -1;
    bool is_offerer_ = false;
    std::string session_id_;
    std::string last_offer_sdp_;
    std::string last_answer_sdp_;
    std::string local_description_type_;
    std::deque<std::vector<uint8_t>> received_packets_;
};

}  // namespace core_webrtc
