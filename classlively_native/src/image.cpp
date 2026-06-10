// classlively_native/src/image.cpp
// Direct2D 1.1 GPU 高斯模糊
//
// 输入像素 → D2D 源Bitmap → GaussianBlur Effect → DXGI Surface 渲染 → Texture Map 读回
//
// 参考：
//   https://learn.microsoft.com/en-us/windows/win32/direct2d/gaussian-blur
//   https://learn.microsoft.com/en-us/windows/win32/direct2d/how-to-create-a-bitmap-from-wic

#include <pybind11/pybind11.h>
#include <pybind11/buffer_info.h>
#include "image_blur.h"
#include <vector>
#include <cstdio>
#include <cstring>

#include <d2d1_1.h>
#include <d2d1effects.h>
#include <d3d11.h>
#include <dxgi1_2.h>

#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dxguid.lib")

namespace py = pybind11;

static ID2D1Factory1*   g_d2d_factory  = nullptr;
static ID3D11Device*    g_d3d_device   = nullptr;
static ID3D11DeviceContext* g_d3d_ctx   = nullptr;  // Immediate Context
static ID2D1Device*     g_d2d_device   = nullptr;
static bool             g_initialized  = false;

static bool ensure_init() {
    if (g_initialized) return true;

    HRESULT hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &g_d2d_factory);
    if (FAILED(hr)) { fprintf(stderr, "[native] D2D1Factory: 0x%08X\n", hr); return false; }

    D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0 };
    hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
                           D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 1, D3D11_SDK_VERSION,
                           &g_d3d_device, nullptr, nullptr);
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
                               D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 1, D3D11_SDK_VERSION,
                               &g_d3d_device, nullptr, nullptr);
        if (FAILED(hr)) { fprintf(stderr, "[native] D3D11: 0x%08X\n", hr); return false; }
    }

    // 获取 Immediate Context— HW/WARP 都用
    g_d3d_device->GetImmediateContext(&g_d3d_ctx);

    IDXGIDevice* dxgi_dev = nullptr;
    hr = g_d3d_device->QueryInterface(__uuidof(IDXGIDevice), (void**)&dxgi_dev);
    if (FAILED(hr)) { fprintf(stderr, "[native] QI DXGI: 0x%08X\n", hr); return false; }

    hr = g_d2d_factory->CreateDevice(dxgi_dev, &g_d2d_device);
    dxgi_dev->Release();
    if (FAILED(hr)) { fprintf(stderr, "[native] D2DDevice: 0x%08X\n", hr); return false; }

    g_initialized = true;
    return true;
}

std::vector<uint8_t> gaussian_blur_d2d(const uint8_t* bgra_data, int width, int height, float radius) {
    const size_t data_size = (size_t)width * height * 4;
    std::vector<uint8_t> output(data_size);

    if (radius <= 0 || width <= 0 || height <= 0 ||
        !ensure_init()) {
        std::copy(bgra_data, bgra_data + data_size, output.begin());
        return output;
    }

    HRESULT hr;
    ID2D1DeviceContext* dc = nullptr;
    hr = g_d2d_device->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &dc);
    if (FAILED(hr)) { fprintf(stderr, "[native] DC: 0x%08X\n", hr); goto fail; }

    // DXGI Texture
    D3D11_TEXTURE2D_DESC tex_desc = {};
    tex_desc.Width              = (UINT)width;
    tex_desc.Height             = (UINT)height;
    tex_desc.MipLevels         = 1;
    tex_desc.ArraySize          = 1;
    tex_desc.Format             = DXGI_FORMAT_B8G8R8A8_UNORM;  // 与 BGRA 对应
    tex_desc.SampleDesc.Count   = 1;
    tex_desc.SampleDesc.Quality = 0;
    tex_desc.Usage              = D3D11_USAGE_DEFAULT;
    tex_desc.BindFlags          = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    tex_desc.CPUAccessFlags     = 0;
    tex_desc.MiscFlags          = 0;

    ID3D11Texture2D* tex = nullptr;
    hr = g_d3d_device->CreateTexture2D(&tex_desc, nullptr, &tex);
    if (FAILED(hr)) { fprintf(stderr, "[native] Texture: 0x%08X\n", hr); dc->Release(); goto fail; }

    // 获取 DXGI Surface 创建 D2D Bitmap
    IDXGISurface* dxgi_surface = nullptr;
    hr = tex->QueryInterface(__uuidof(IDXGISurface), (void**)&dxgi_surface);
    if (FAILED(hr)) { fprintf(stderr, "[native] QI Surface: 0x%08X\n", hr); tex->Release(); dc->Release(); goto fail; }

    D2D1_BITMAP_PROPERTIES1 bmp_props = {};
    bmp_props.pixelFormat.format    = DXGI_FORMAT_B8G8R8A8_UNORM;
    bmp_props.pixelFormat.alphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED;
    bmp_props.dpiX = 96.0f;
    bmp_props.dpiY = 96.0f;
    bmp_props.bitmapOptions          = D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW;

    ID2D1Bitmap1* target_bmp = nullptr;
    D2D1_SIZE_U size_u = { (UINT32)width, (UINT32)height };
    hr = dc->CreateBitmapFromDxgiSurface(dxgi_surface, &bmp_props, &target_bmp);
    if (FAILED(hr)) { fprintf(stderr, "[native] TargetBmp: 0x%08X\n", hr); dxgi_surface->Release(); tex->Release(); dc->Release(); goto fail; }
    dc->SetTarget(target_bmp);

    // 源 Bitmap
    D2D1_BITMAP_PROPERTIES1 src_props = {};
    src_props.pixelFormat.format    = DXGI_FORMAT_B8G8R8A8_UNORM;
    src_props.pixelFormat.alphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED;
    src_props.dpiX = 96.0f;
    src_props.dpiY = 96.0f;

    ID2D1Bitmap1* src_bmp = nullptr;
    hr = dc->CreateBitmap(size_u, bgra_data, (UINT32)(width * 4), &src_props, &src_bmp);
    if (FAILED(hr)) { fprintf(stderr, "[native] SrcBmp: 0x%08X\n", hr); target_bmp->Release(); dxgi_surface->Release(); tex->Release(); dc->Release(); goto fail; }

    // GaussianBlur Effect
    ID2D1Effect* blur = nullptr;
    hr = dc->CreateEffect(CLSID_D2D1GaussianBlur, &blur);
    if (FAILED(hr)) { fprintf(stderr, "[native] Effect: 0x%08X\n", hr); src_bmp->Release(); target_bmp->Release(); dxgi_surface->Release(); tex->Release(); dc->Release(); goto fail; }

    blur->SetValue(D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION, radius);
    blur->SetValue(D2D1_GAUSSIANBLUR_PROP_BORDER_MODE, D2D1_BORDER_MODE_HARD);
    blur->SetInput(0, src_bmp);

    // 渲染
    dc->BeginDraw();
    dc->Clear(D2D1::ColorF(0, 0, 0, 0));
    dc->DrawImage(blur);
    hr = dc->EndDraw();
    if (FAILED(hr)) {
        fprintf(stderr, "[native] EndDraw: 0x%08X\n", hr);
        blur->Release(); src_bmp->Release(); target_bmp->Release(); dxgi_surface->Release(); tex->Release(); dc->Release();
        goto fail;
    }

    // 读回
    D3D11_TEXTURE2D_DESC staging_desc = tex_desc;
    staging_desc.Usage          = D3D11_USAGE_STAGING;
    staging_desc.BindFlags      = 0;
    staging_desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    staging_desc.MiscFlags      = 0;

    ID3D11Texture2D* staging = nullptr;
    hr = g_d3d_device->CreateTexture2D(&staging_desc, nullptr, &staging);
    if (FAILED(hr)) {
        fprintf(stderr, "[native] StagingTex: 0x%08X\n", hr);
        blur->Release(); src_bmp->Release(); target_bmp->Release(); dxgi_surface->Release(); tex->Release(); dc->Release();
        goto fail;
    }

    // CopyResource: GPU Texture → Staging (CPU-readable)
    g_d3d_ctx->CopyResource(staging, tex);

    // Map staging texture 读取像素
    D3D11_MAPPED_SUBRESOURCE mapped = {};
    hr = g_d3d_ctx->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
    if (SUCCEEDED(hr)) {
        const UINT32 row_bytes = width * 4;
        for (int y = 0; y < height; ++y) {
            memcpy(output.data() + (size_t)y * row_bytes,
                   (const uint8_t*)mapped.pData + (size_t)y * mapped.RowPitch,
                   row_bytes);
        }
        g_d3d_ctx->Unmap(staging, 0);
    } else {
        fprintf(stderr, "[native] MapStaging: 0x%08X\n", hr);
    }

    staging->Release();
    blur->Release();
    src_bmp->Release();
    target_bmp->Release();
    dxgi_surface->Release();
    tex->Release();
    dc->Release();

    return output;

fail:
    std::copy(bgra_data, bgra_data + data_size, output.begin());
    return output;
}

py::bytes classlively_native::blur_image_py(py::buffer input, int width, int height, float radius) {
    py::buffer_info info = input.request();
    if (info.ndim > 3) throw std::runtime_error("input must be raw bytes");
    auto result = gaussian_blur_d2d(static_cast<const uint8_t*>(info.ptr), width, height, radius);
    return py::bytes(reinterpret_cast<const char*>(result.data()), result.size());
}
