# core/api_server.py
"""
ClassLively Python 服
fastapi 暴露为 REST API，供 C# WinUI 3 前端调用。
"""

from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from fastapi import FastAPI, HTTPException, Query
from fastapi.responses import FileResponse
from pydantic import BaseModel
from typing import Any, Optional
import json
import threading

from core.config import cfg
from core.constants import WALLPAPER_DIR, BASE_DIR, APP_NAME, VERSION, BUILD_DATE
from core.logger import logger

app = FastAPI(title="ClassLively Backend", version="1.0.0")

# 延迟导入

_wallpaper_interface = None
_home_interface = None
_download_interface = None


def _get_wallpaper():
    global _wallpaper_interface
    if _wallpaper_interface is None:
        from ui.wallpaper import WallpaperInterface
        _wallpaper_interface = WallpaperInterface()
    return _wallpaper_interface


def _get_home():
    global _home_interface
    if _home_interface is None:
        from ui.home import HomeInterface
        _home_interface = HomeInterface()
    return _home_interface


def _get_download():
    global _download_interface
    if _download_interface is None:
        from ui.download import DownloadInterface
        _download_interface = DownloadInterface()
    return _download_interface


# ── 响应模型 ──

class ApiResponse(BaseModel):
    code: int = 0
    message: str = "ok"
    data: Any = None


class ConfigSetRequest(BaseModel):
    value: Any


# ── 健康检查 ──

@app.get("/api/health", tags=["system"])
async def health_check() -> ApiResponse:
    return ApiResponse(data={"status": "ok", "app": APP_NAME, "version": VERSION})


# ── 配置 API ──

@app.get("/api/config", tags=["config"])
async def get_config() -> ApiResponse:
    """获取全部配置项的当前值"""
    try:
        data = {}
        for item in cfg.items.values():
            data[item.name] = item.value
        return ApiResponse(data=data)
    except Exception as e:
        logger.error(f"[API] get_config error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.post("/api/config/{key}", tags=["config"])
async def set_config(key: str, req: ConfigSetRequest) -> ApiResponse:
    """设置单个配置项"""
    try:
        item = cfg.items.get(key)
        if item is None:
            return ApiResponse(code=404, message=f"Config key not found: {key}")
        item.value = req.value
        return ApiResponse(message=f"Set {key} = {req.value}")
    except Exception as e:
        logger.error(f"[API] set_config({key}) error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.get("/api/config/items", tags=["config"])
async def list_config_items() -> ApiResponse:
    """列出所有可用的配置项元信息"""
    try:
        items = []
        for name, item in cfg.items.items():
            items.append({
                "key": name,
                "value": item.value,
                "type": type(item.value).__name__
            })
        return ApiResponse(data=items)
    except Exception as e:
        logger.error(f"[API] list_config_items error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 壁纸 API ──

@app.get("/api/wallpaper/current", tags=["wallpaper"])
async def get_current_wallpaper() -> ApiResponse:
    """获取当前壁纸信息"""
    try:
        wp = _get_wallpaper()
        path = getattr(wp, 'currentWallpaperPath', None) or ""
        return ApiResponse(data={
            "path": path,
            "exists": os.path.exists(path) if path else False
        })
    except Exception as e:
        logger.error(f"[API] get_current_wallpaper error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.post("/api/wallpaper/fetch", tags=["wallpaper"])
async def fetch_wallpaper(source: Optional[str] = Query(None)) -> ApiResponse:
    """获取一张新壁纸"""
    try:
        wp = _get_wallpaper()
        # 调用现有的获取逻辑
        if hasattr(wp, '_onGetClicked'):
            wp._onGetClicked()
        return ApiResponse(message="fetch triggered")
    except Exception as e:
        logger.error(f"[API] fetch_wallpaper error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.post("/api/wallpaper/set-desktop", tags=["wallpaper"])
async def set_desktop_wallpaper(path: str) -> ApiResponse:
    """设为桌面壁纸"""
    try:
        from classlively_native import set_wallpaper
        result = set_wallpaper(path)
        return ApiResponse(data={"success": bool(result)})
    except Exception as e:
        logger.error(f"[API] set_desktop_wallpaper error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.get("/api/wallpaper/history", tags=["wallpaper"])
async def get_history(page: int = Query(1, ge=1), per_page: int = Query(20, ge=1, le=100)) -> ApiResponse:
    """获取壁纸历史记录"""
    try:
        history_dir = WALLPAPER_DIR
        if not os.path.exists(history_dir):
            return ApiResponse(data=[])

        files = [f for f in os.listdir(history_dir)
                 if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp', '.webp'))]
        files.sort(reverse=True)

        start = (page - 1) * per_page
        end = start + per_page
        page_files = files[start:end]

        items = []
        for f in page_files:
            full_path = os.path.join(history_dir, f)
            items.append({
                "path": full_path,
                "filename": f,
                "size": os.path.getsize(full_path),
                "mtime": os.path.getmtime(full_path)
            })

        return ApiResponse(data={
            "items": items,
            "total": len(files),
            "page": page,
            "per_page": per_page
        })
    except Exception as e:
        logger.error(f"[API] get_history error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.get("/api/wallpaper/blurred", tags=["wallpaper"])
async def get_blurred_wallpaper(path: str = Query(...)) -> FileResponse:
    """调用 C++ blur_image返回模糊后的壁纸"""
    import io
    from classlively_native import blur_image_py
    from PIL import Image as PILImage

    if not os.path.exists(path):
        raise HTTPException(status_code=404, detail=f"File not found: {path}")

    # 原图 → C++ 模糊 → 返回 JPEG 字节
    radius = getattr(cfg.blur_radius, 'value', 30)
    img = PILImage.open(path).convert('RGBA')
    blurred_bytes = blur_image_py(img, radius)
    # 模糊结果写入临时文件供 FileResponse 返回
    buf = io.BytesIO()
    result_img = PILImage.frombytes('RGBA', img.size, blurred_bytes)
    result_img.save(buf, format='JPEG')
    buf.seek(0)

    # 写入临时文件
    tmp_path = os.path.join(BASE_DIR, '_tmp_blurred.jpg')
    with open(tmp_path, 'wb') as f:
        f.write(buf.getvalue())

    return FileResponse(tmp_path, media_type='image/jpeg')


# ── 天气 API ──

@app.get("/api/weather", tags=["weather"])
async def get_weather(city: Optional[str] = Query(None)) -> ApiResponse:
    """获取天气信息"""
    try:
        from services.weather import WeatherService
        ws = WeatherService()
        city_name = city or cfg.city.value
        info = ws.get_weather(city_name)
        if info is None:
            return ApiResponse(code=404, message="Weather data not available")
        return ApiResponse(data={
            "temp": getattr(info, 'temperature', ''),
            "icon": getattr(info, 'icon_code', ''),
            "description": getattr(info, 'description', ''),
            "city": city_name
        })
    except Exception as e:
        logger.error(f"[API] get_weather error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 一言 API ──

@app.get("/api/poetry", tags=["poetry"])
async def get_poetry() -> ApiResponse:
    """获取一言/诗词"""
    try:
        from services.poetry import PoetryService
        text = PoetryService.get_poetry()
        return ApiResponse(data={"text": text})
    except Exception as e:
        logger.error(f"[API] get_poetry error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 系统 API ──

@app.get("/api/system/idle-ms", tags=["system"])
async def idle_ms() -> ApiResponse:
    """获取系统空闲毫秒数"""
    try:
        from classlively_native import idle_get_milliseconds
        ms = idle_get_milliseconds()
        return ApiResponse(data={"ms": ms})
    except Exception as e:
        logger.error(f"[API] idle_ms error: {e}")
        return ApiResponse(code=500, message=str(e), data={"ms": -1})


# ── 媒体 API ──

import base64

@app.get("/api/media/info", tags=["media"])
async def media_info() -> ApiResponse:
    """获取当前媒体播放信息"""
    try:
        from services.media import get_media_info
        info = get_media_info()
        if info is None:
            return ApiResponse(data={})

        thumb_b64 = ""
        if hasattr(info, 'thumbnail_data') and info.thumbnail_data:
            try:
                thumb_b64 = base64.b64encode(info.thumbnail_data).decode('ascii')
            except Exception:
                pass

        return ApiResponse(data={
            "title": getattr(info, 'title', '') or "",
            "artist": getattr(info, 'artist', '') or "",
            "album": getattr(info, 'album', '') or "",
            "position_ms": getattr(info, 'position_ms', 0),
            "duration_ms": getattr(info, 'duration_ms', 0),
            "is_playing": getattr(info, 'is_playing', False),
            "app_name": getattr(info, 'app_name', '') or "",
            "song_id": getattr(info, 'song_id', '') or "",
            "thumbnail_base64": thumb_b64,
        })
    except Exception as e:
        logger.error(f"[API] media_info error: {e}")
        return ApiResponse(code=500, message=str(e), data={})


@app.get("/api/media/detail", tags=["media"])
async def media_detail(title: str = Query(""), artist: str = Query("")) -> ApiResponse:
    """获取歌曲详情"""
    try:
        from services.media import fetch_all_info
        result = fetch_all_info(title.strip(), artist.strip())

        # 封面 → base64
        cover_b64 = ""
        cover = result.get('cover')
        if cover and isinstance(cover, (bytes, bytearray)):
            try:
                cover_b64 = base64.b64encode(cover).decode('ascii')
            except Exception:
                pass

        # 歌词文本
        lyrics_obj = result.get('lyrics')
        lyrics_text = ""
        if lyrics_obj and hasattr(lyrics_obj, 'raw_lrc'):
            lyrics_text = lyrics_obj.raw_lrc
        elif isinstance(lyrics_obj, str):
            lyrics_text = lyrics_obj

        # 歌曲详情
        detail = result.get('detail')
        detail_data = {}
        if detail:
            detail_data = {
                "song_id": getattr(detail, 'song_id', None),
                "name": getattr(detail, 'name', ''),
                "artists": getattr(detail, 'artists', []),
                "album_name": getattr(detail, 'album_name', ''),
                "cover_url": getattr(detail, 'cover_url', ''),
                "duration": getattr(detail, 'duration', 0),
            }

        return ApiResponse(data={
            "song_id": result.get('song_id'),
            "detail": detail_data,
            "lyrics": lyrics_text,
            "cover_base64": cover_b64,
        })
    except Exception as e:
        logger.error(f"[API] media_detail error: {e}")
        return ApiResponse(code=500, message=str(e), data={})


# ── 下载 API ──

@app.get("/api/software/list", tags=["software"])
async def software_list(category: Optional[str] = Query(None)) -> ApiResponse:
    """获取软件列表"""
    try:
        from data.software_list import SOFTWARE_CATEGORIES
        if category and category in SOFTWARE_CATEGORIES:
            apps = SOFTWARE_CATEGORIES[category]
        else:
            apps = []
            for cat_apps in SOFTWARE_CATEGORIES.values():
                apps.extend(cat_apps)

        items = []
        for app_data in apps:
            items.append({
                "name": app_data.get("name", ""),
                "description": app_data.get("description", ""),
                "icon": app_data.get("icon", ""),
                "link": app_data.get("link", ""),
                "category": category or ""
            })
        return ApiResponse(data=items)
    except Exception as e:
        logger.error(f"[API] software_list error: {e}")
        return ApiResponse(code=500, message=str(e), data=[])


@app.post("/api/software/download/{name}", tags=["software"])
async def software_download(name: str) -> ApiResponse:
    """触发软件下载"""
    try:
        dl = _get_download()
        if hasattr(dl, '_startDownload'):
            dl._startDownload(name)
        return ApiResponse(message=f"Download triggered: {name}")
    except Exception as e:
        logger.error(f"[API] software_download error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 启动入口 ──

if __name__ == "__main__":
    import uvicorn
    logger.info("[API Server] Starting ClassLively Backend on http://127.0.0.1:19856")
    uvicorn.run(app, host="127.0.0.1", port=19856, log_level="info")
