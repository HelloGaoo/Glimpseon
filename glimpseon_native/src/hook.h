#pragma once
#include <pybind11/pybind11.h>

namespace Glimpseon_native {
    void install_hook();
    void uninstall_hook();
    bool was_page_operation_recent(int ms_threshold);
}
