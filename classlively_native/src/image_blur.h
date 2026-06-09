// classlively_native/src/image_blur.h
#pragma once
#include <pybind11/pybind11.h>

namespace classlively_native {
    pybind11::bytes blur_image_py(pybind11::buffer input, int width, int height, float radius);
}
