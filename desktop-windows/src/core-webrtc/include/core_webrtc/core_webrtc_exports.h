#pragma once

#include <cstddef>
#include <cstdint>

#ifdef _WIN32
#define CORE_WEBRTC_EXPORT __declspec(dllexport)
#else
#define CORE_WEBRTC_EXPORT
#endif

extern "C" {

struct core_webrtc_handle;

struct core_webrtc_diagnostics {
    const char* path_type;
    int local_candidates_count;
    const char* selected_candidate_pair_type;
    const char* failure_hint;
};

struct core_webrtc_offer_result {
    int success;
    const char* error_message;
    const char* session_id;
    const char* offer_sdp;
    const char* fingerprint;
    core_webrtc_diagnostics diagnostics;
};

struct core_webrtc_answer_result {
    int success;
    const char* error_message;
    const char* answer_sdp;
    const char* fingerprint;
    core_webrtc_diagnostics diagnostics;
};

struct core_webrtc_apply_result {
    int success;
    const char* error_message;
    core_webrtc_diagnostics diagnostics;
};

CORE_WEBRTC_EXPORT core_webrtc_handle* core_webrtc_create();
CORE_WEBRTC_EXPORT void core_webrtc_destroy(core_webrtc_handle* handle);

CORE_WEBRTC_EXPORT core_webrtc_offer_result core_webrtc_create_offer(core_webrtc_handle* handle);
CORE_WEBRTC_EXPORT core_webrtc_answer_result core_webrtc_create_answer(core_webrtc_handle* handle, const char* offer_sdp);
CORE_WEBRTC_EXPORT core_webrtc_apply_result core_webrtc_apply_answer(core_webrtc_handle* handle, const char* answer_sdp);
CORE_WEBRTC_EXPORT int core_webrtc_send_pcm_frame(core_webrtc_handle* handle, const uint8_t* data, std::size_t size);
CORE_WEBRTC_EXPORT int core_webrtc_pop_pcm_frame(
    core_webrtc_handle* handle,
    uint8_t* out_buffer,
    std::size_t capacity,
    std::size_t* out_size
);
CORE_WEBRTC_EXPORT core_webrtc_diagnostics core_webrtc_get_diagnostics(core_webrtc_handle* handle);
CORE_WEBRTC_EXPORT int core_webrtc_has_libdatachannel();
CORE_WEBRTC_EXPORT void core_webrtc_close(core_webrtc_handle* handle);

CORE_WEBRTC_EXPORT void core_webrtc_free_string(const char* value);

}  // extern "C"
