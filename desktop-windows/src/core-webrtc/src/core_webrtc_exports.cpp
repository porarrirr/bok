#include "core_webrtc/core_webrtc_exports.h"

#include "core_webrtc/session_controller.h"

#include <cstdlib>
#include <cstring>
#include <exception>
#include <string>
#include <vector>

struct core_webrtc_handle {
    core_webrtc::session_controller controller;
};

namespace {

const char* copy_string(const std::string& value) {
    auto* memory = static_cast<char*>(std::malloc(value.size() + 1));
    if (memory == nullptr) {
        return nullptr;
    }
    std::memcpy(memory, value.c_str(), value.size() + 1);
    return memory;
}

core_webrtc_diagnostics to_export(const core_webrtc::diagnostics& diagnostics) {
    core_webrtc_diagnostics result{};
    result.path_type = copy_string(diagnostics.path_type);
    result.local_candidates_count = diagnostics.local_candidates_count;
    result.selected_candidate_pair_type = copy_string(diagnostics.selected_candidate_pair_type);
    result.failure_hint = copy_string(diagnostics.failure_hint);
    return result;
}

core_webrtc_offer_result offer_failure(const std::string& message) {
    core_webrtc_offer_result result{};
    result.success = 0;
    result.error_message = copy_string(message);
    result.session_id = copy_string("");
    result.offer_sdp = copy_string("");
    result.fingerprint = copy_string("");
    result.diagnostics = to_export(core_webrtc::diagnostics{});
    return result;
}

core_webrtc_answer_result answer_failure(const std::string& message) {
    core_webrtc_answer_result result{};
    result.success = 0;
    result.error_message = copy_string(message);
    result.answer_sdp = copy_string("");
    result.fingerprint = copy_string("");
    result.diagnostics = to_export(core_webrtc::diagnostics{});
    return result;
}

core_webrtc_apply_result apply_failure(const std::string& message) {
    core_webrtc_apply_result result{};
    result.success = 0;
    result.error_message = copy_string(message);
    result.diagnostics = to_export(core_webrtc::diagnostics{});
    return result;
}

}  // namespace

extern "C" {

core_webrtc_handle* core_webrtc_create() {
    try {
        return new core_webrtc_handle();
    } catch (...) {
        return nullptr;
    }
}

void core_webrtc_destroy(core_webrtc_handle* handle) {
    delete handle;
}

core_webrtc_offer_result core_webrtc_create_offer(core_webrtc_handle* handle) {
    if (handle == nullptr) {
        return offer_failure("Controller is null");
    }
    try {
        auto local_offer = handle->controller.create_offer();
        core_webrtc_offer_result result{};
        result.success = 1;
        result.error_message = copy_string("");
        result.session_id = copy_string(local_offer.session_id);
        result.offer_sdp = copy_string(local_offer.offer_sdp);
        result.fingerprint = copy_string(local_offer.fingerprint);
        result.diagnostics = to_export(local_offer.info);
        return result;
    } catch (const std::exception& ex) {
        return offer_failure(ex.what());
    } catch (...) {
        return offer_failure("Unknown offer error");
    }
}

core_webrtc_answer_result core_webrtc_create_answer(core_webrtc_handle* handle, const char* offer_sdp) {
    if (handle == nullptr) {
        return answer_failure("Controller is null");
    }
    if (offer_sdp == nullptr) {
        return answer_failure("offer_sdp is null");
    }
    try {
        auto local_answer = handle->controller.create_answer(offer_sdp);
        core_webrtc_answer_result result{};
        result.success = 1;
        result.error_message = copy_string("");
        result.answer_sdp = copy_string(local_answer.answer_sdp);
        result.fingerprint = copy_string(local_answer.fingerprint);
        result.diagnostics = to_export(local_answer.info);
        return result;
    } catch (const std::exception& ex) {
        return answer_failure(ex.what());
    } catch (...) {
        return answer_failure("Unknown answer error");
    }
}

core_webrtc_apply_result core_webrtc_apply_answer(core_webrtc_handle* handle, const char* answer_sdp) {
    if (handle == nullptr) {
        return apply_failure("Controller is null");
    }
    if (answer_sdp == nullptr) {
        return apply_failure("answer_sdp is null");
    }
    try {
        const auto success = handle->controller.apply_answer(answer_sdp);
        core_webrtc_apply_result result{};
        result.success = success ? 1 : 0;
        result.error_message = copy_string(success ? "" : "Failed to apply answer");
        result.diagnostics = to_export(handle->controller.get_diagnostics());
        return result;
    } catch (const std::exception& ex) {
        return apply_failure(ex.what());
    } catch (...) {
        return apply_failure("Unknown apply error");
    }
}

int core_webrtc_send_pcm_frame(core_webrtc_handle* handle, const uint8_t* data, std::size_t size) {
    if (handle == nullptr || data == nullptr || size == 0) {
        return 0;
    }
    return handle->controller.send_pcm_frame(data, size) ? 1 : 0;
}

int core_webrtc_pop_pcm_frame(core_webrtc_handle* handle, uint8_t* out_buffer, std::size_t capacity, std::size_t* out_size) {
    if (out_size == nullptr) {
        return 0;
    }
    *out_size = 0;

    if (handle == nullptr) {
        return 0;
    }

    try {
        std::vector<uint8_t> packet;
        if (!handle->controller.try_pop_received_pcm(packet)) {
            return 0;
        }

        *out_size = packet.size();
        if (out_buffer == nullptr || capacity < packet.size()) {
            return -1;
        }

        std::memcpy(out_buffer, packet.data(), packet.size());
        return 1;
    } catch (...) {
        *out_size = 0;
        return 0;
    }
}

core_webrtc_diagnostics core_webrtc_get_diagnostics(core_webrtc_handle* handle) {
    if (handle == nullptr) {
        core_webrtc::diagnostics diagnostics;
        diagnostics.failure_hint = "controller_null";
        return to_export(diagnostics);
    }
    return to_export(handle->controller.get_diagnostics());
}

void core_webrtc_close(core_webrtc_handle* handle) {
    if (handle == nullptr) {
        return;
    }
    handle->controller.close();
}

void core_webrtc_free_string(const char* value) {
    std::free(const_cast<char*>(value));
}

}  // extern "C"
