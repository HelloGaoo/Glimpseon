# Glimpseon
# Copyright (C) 2026 HelloGaoo
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import asyncio
import atexit
import faulthandler
import functools
import inspect
import logging
import logging.handlers
import os
import platform
import signal
import sys
import threading
import traceback
import zipfile
from concurrent.futures import ThreadPoolExecutor, ProcessPoolExecutor, Future
from datetime import datetime
from typing import Optional, Dict, Any

from PyQt6.QtCore import qInstallMessageHandler, QtMsgType

from core.constants import APP_NAME, BASE_DIR

#if getattr(sys, 'frozen', False):
#    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
#else:
#    BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

log_dir = os.path.join(BASE_DIR, "logs")
if not os.path.exists(log_dir): os.makedirs(log_dir)

DEFAULT_LOG_LEVEL = logging.INFO
LOG_FORMAT = '%(precise_time)s|%(levelname)s|%(caller_info)s|%(module)s:%(lineno)d|%(message)s'
DATE_FORMAT = '%Y-%m-%d %H:%M:%S'
LOG_MAX_BYTES = 1 * 1024 * 1024

_faulthandler_file = None
_original_sig_handlers: Dict[signal.Signals, Any] = {}
_psutil_available: Optional[bool] = None
_log_lock = threading.RLock()


def _safe_call(func, default="", log_errors=False):
    try:
        return func()
    except Exception as e:
        if log_errors:
            try: print(f"[logger] safe_call error: {e}", file=sys.stderr)
            except Exception: pass
        return default


def _get_system_context() -> str:
    global _psutil_available
    ctx = []
    ctx.append(f"时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    ctx.append(f"PID: {os.getpid()}")
    ctx.append(f"Python: {platform.python_version()}")
    ctx.append(f"系统: {platform.system()} {platform.release()}")
    if _psutil_available is None:
        _psutil_available = _safe_call(lambda: __import__('psutil'), default=None) is not None
    if _psutil_available:
        def _get_psutil_info():
            import psutil
            lines = []
            mem = psutil.virtual_memory()
            lines.append(f"内存: {mem.percent}% ({mem.used // 1024 // 1024}MB / {mem.total // 1024 // 1024}MB)")
            lines.append(f"CPU: {psutil.cpu_percent(interval=0.1)}%")
            disk = psutil.disk_usage(BASE_DIR)
            lines.append(f"磁盘: {disk.percent}% ({disk.used // 1024 // 1024 // 1024}GB / {disk.total // 1024 // 1024 // 1024}GB)")
            return '\n'.join(lines)
        psutil_info = _safe_call(_get_psutil_info)
        if psutil_info: ctx.append(psutil_info)
    ctx.append(f"线程数: {threading.active_count()}")
    ctx.append(f"线程: {[t.name for t in threading.enumerate()]}")
    return '\n'.join(ctx)


def _format_exception_chain(exc: BaseException) -> str:
    lines = []
    seen = set()
    current = exc
    depth = 0
    while current is not None and depth < 15:
        if id(current) in seen:
            lines.append("  " * depth + "└─ [循环引用]")
            break
        seen.add(id(current))
        prefix = "  " * depth + ("└─ " if depth > 0 else "")
        tb = current.__traceback__
        if tb:
            tb_str = ''.join(traceback.format_exception(type(current), current, tb))
        else:
            tb_str = f"{type(current).__name__}: {current}"
        lines.append(f"{prefix}{tb_str.strip()}")
        if current.__suppress_context__:
            break
        current = current.__context__ or current.__cause__
        if current and not current.__suppress_context__:
            depth += 1
        else:
            break
    return '\n'.join(lines)


class CustomLogger(logging.Logger):
    def _log(self, level, msg, args, exc_info=None, extra=None, stack_info=False, stacklevel=3):
        with _log_lock:
            try:
                caller_frame = inspect.stack()[stacklevel]
                module_name = caller_frame[1].split(os.sep)[-1].replace('.py', '')
                function_name = caller_frame[3]
                class_name = ''
                frame = caller_frame[0]
                try:
                    if 'self' in frame.f_locals: class_name = frame.f_locals['self'].__class__.__name__
                    elif 'cls' in frame.f_locals: class_name = frame.f_locals['cls'].__name__
                except Exception:
                    pass
                if class_name:
                    caller_info = f"{APP_NAME}.{class_name}.{function_name}"
                elif function_name == '<module>':
                    caller_info = f"{APP_NAME}.Main.<module>"
                else:
                    caller_info = f"{APP_NAME}.{function_name}"
            except Exception:
                caller_info = f"{APP_NAME}.Unknown"
            if extra is None: extra = {}
            extra['caller_info'] = caller_info
            try:
                from core.utils import precise_time_str
                extra['precise_time'] = precise_time_str()
            except Exception:
                extra['precise_time'] = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
            super()._log(level, msg, args, exc_info, extra, stack_info, stacklevel)

logging.setLoggerClass(CustomLogger)


class Logger:
    def __init__(self, disable_log=False, log_level="INFO", max_count=50, max_days=7, compress_logs=True):
        self.logger = logging.getLogger(APP_NAME)
        self.disable_log = disable_log
        self.log_level = log_level
        self.max_count = max_count
        self.max_days = max_days
        self.compress_logs = compress_logs
        self.file_handler = None
        self.console_handler = None
        self._qt_handler_installed = False
        self._hooks_installed = False
        self._log_timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.__setup_handlers()

    def __compress_old_logs(self):
        if not self.compress_logs: return
        for file in _safe_call(lambda: os.listdir(log_dir), default=[]):
            if not file.endswith('.log') or file.endswith('.zip'): continue
            file_path = os.path.join(log_dir, file)
            if not _safe_call(lambda: os.path.isfile(file_path)): continue
            mtime = _safe_call(lambda: os.path.getmtime(file_path))
            if mtime is None: continue
            age_hours = (datetime.now().timestamp() - mtime) / 3600
            if age_hours > 24:
                zip_path = file_path + '.zip'
                try:
                    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zf:
                        zf.write(file_path, file)
                    os.remove(file_path)
                except Exception as e:
                    try: print(f"[logger] 压缩失败 {file}: {e}", file=sys.stderr)
                    except Exception: pass

    def __clean_oldlog(self):
        if not os.path.exists(log_dir): return
        self.__compress_old_logs()
        log_files = []
        for file in _safe_call(lambda: os.listdir(log_dir), default=[]):
            if (file.startswith("app_") or file.startswith("crash_")) and (file.endswith(".log") or file.endswith(".zip")):
                file_path = os.path.join(log_dir, file)
                if _safe_call(lambda: os.path.isfile(file_path)):
                    mtime = _safe_call(lambda: os.path.getmtime(file_path))
                    if mtime: log_files.append((file_path, mtime))
        log_files.sort(key=lambda x: x[1], reverse=True)
        if len(log_files) > self.max_count:
            for file_path, _ in log_files[self.max_count:]:
                _safe_call(lambda: os.remove(file_path))
        cutoff_time = datetime.now().timestamp() - (self.max_days * 24 * 3600)
        for file_path, mtime in log_files:
            if mtime < cutoff_time:
                _safe_call(lambda: os.remove(file_path))

    def __setup_handlers(self):
        for handler in self.logger.handlers[:]:
            self.logger.removeHandler(handler)
        if self.disable_log: return
        log_level = getattr(logging, self.log_level.upper(), logging.INFO)
        self.logger.setLevel(log_level)
        self.file_handler = logging.handlers.RotatingFileHandler(
            os.path.join(log_dir, f"app_{self._log_timestamp}.log"),
            encoding="utf-8", maxBytes=LOG_MAX_BYTES, backupCount=self.max_count
        )
        self.file_handler.setLevel(log_level)
        self.console_handler = logging.StreamHandler()
        self.console_handler.setLevel(log_level)
        formatter = logging.Formatter(
            LOG_FORMAT,
            datefmt='%Y-%m-%d %H:%M:%S'
        )
        self.file_handler.setFormatter(formatter)
        self.console_handler.setFormatter(formatter)
        self.logger.addHandler(self.file_handler)
        self.logger.addHandler(self.console_handler)
        self.__clean_oldlog()

    def update_cfg(self, disable_log=False, log_level="INFO", max_count=50, max_days=7, compress_logs=True):
        self.disable_log = disable_log
        self.log_level = log_level
        self.max_count = max_count
        self.max_days = max_days
        self.compress_logs = compress_logs
        self.__setup_handlers()

    def debug(self, message): self.logger.debug(message)
    def info(self, message): self.logger.info(message)
    def warning(self, message): self.logger.warning(message)
    def error(self, message, exc_info=None): self.logger.error(message, exc_info=exc_info)
    def exception(self, message): self.logger.exception(message)
    def critical(self, message, exc_info=None): self.logger.critical(message, exc_info=exc_info)


logger = Logger()


def _install_faulthandler():
    global _faulthandler_file
    try:
        crash_log_path = os.path.join(log_dir, f"crash_{datetime.now().strftime('%Y-%m-%d_%H-%M-%S')}.log")
        _faulthandler_file = open(crash_log_path, "w", encoding="utf-8")
        faulthandler.enable(file=_faulthandler_file, all_threads=True)
        logger.info(f"faulthandler -> {crash_log_path}")
    except Exception as e:
        try:
            faulthandler.enable(all_threads=True)
            logger.warning(f"faulthandler 回退到 stderr: {e}")
        except Exception as e2:
            logger.error(f"faulthandler启用失败: {e2}")


def _install_sys_excepthook():
    _original_excepthook = sys.excepthook
    def custom_exception_hook(exctype, value, tb):
        if issubclass(exctype, KeyboardInterrupt):
            _original_excepthook(exctype, value, tb)
            return
        tb_str = _safe_call(lambda: _format_exception_chain(value), default=str(value))
        ctx = _safe_call(_get_system_context)
        logger.critical(f"[主线程问题] {exctype.__name__}: {value}\n{tb_str}\n\n[上下文]\n{ctx}")
        _original_excepthook(exctype, value, tb)
    sys.excepthook = custom_exception_hook


def _install_threading_excepthook():
    _original = getattr(threading, 'excepthook', None)
    def custom_threading_hook(args):
        exc_type, exc_value, exc_tb = args.exc_type, args.exc_value, args.exc_traceback
        thread_name = args.thread.name if args.thread else "?"
        if exc_type is not None and issubclass(exc_type, KeyboardInterrupt):
            if _original: _original(args)
            return
        tb_str = _safe_call(lambda: _format_exception_chain(exc_value), default=str(exc_value))
        logger.critical(f"[线程问题] {thread_name} | {exc_type.__name__}: {exc_value}\n{tb_str}")
        if _original: _original(args)
    threading.excepthook = custom_threading_hook


def _install_qt_message_handler():
    if logger._qt_handler_installed: return
    try:
        qt_level_map = {
            QtMsgType.QtDebugMsg: logging.DEBUG, QtMsgType.QtInfoMsg: logging.INFO,
            QtMsgType.QtWarningMsg: logging.WARNING, QtMsgType.QtCriticalMsg: logging.ERROR,
            QtMsgType.QtFatalMsg: logging.CRITICAL,
        }
        qt_level_name = {
            QtMsgType.QtDebugMsg: "DEBUG", QtMsgType.QtInfoMsg: "INFO",
            QtMsgType.QtWarningMsg: "WARN", QtMsgType.QtCriticalMsg: "CRIT",
            QtMsgType.QtFatalMsg: "FATAL",
        }
        def qt_message_handler(msg_type, context, message):
            level = qt_level_map.get(msg_type, logging.WARNING)
            level_name = qt_level_name.get(msg_type, "?")
            file_info = ""
            if context.file:
                file_name = context.file.split(os.sep)[-1] if os.sep in context.file else context.file
                file_info = f" [{file_name}:{context.line}]"
            elif context.function: file_info = f" [{context.function}]"
            category = f"Qt.{context.category}" if context.category else "Qt"
            full_message = f"[{category}][{level_name}]{file_info} {message}"
            if level >= logging.CRITICAL: logger.critical(full_message)
            elif level >= logging.ERROR: logger.error(full_message)
            elif level >= logging.WARNING: logger.warning(full_message)
            elif level >= logging.INFO: logger.info(full_message)
            else: logger.debug(full_message)
        qInstallMessageHandler(qt_message_handler)
        logger._qt_handler_installed = True
    except Exception as e:
        logger.error(f"Qt 消息处理器安装失败: {e}")


def _install_asyncio_exception_handler():
    try:
        def custom_asyncio_exception_handler(loop, context):
            exception = context.get('exception')
            message = context.get('message', '未知异步异常')
            if exception:
                tb_str = _safe_call(lambda: _format_exception_chain(exception), default=str(exception))
                logger.critical(f"[asyncio问题] {message} | {type(exception).__name__}: {exception}\n{tb_str}")
            else:
                logger.critical(f"[asyncio问题] {message}")
            loop.default_exception_handler(context)
        policy = asyncio.get_event_loop_policy()
        class CustomEventLoopPolicy(type(policy)):
            def new_event_loop(self):
                loop = super().new_event_loop()
                loop.set_exception_handler(custom_asyncio_exception_handler)
                return loop
        try:
            asyncio.set_event_loop_policy(CustomEventLoopPolicy())
        except Exception:
            try:
                loop = asyncio.get_event_loop()
                if not loop.is_running():
                    loop.set_exception_handler(custom_asyncio_exception_handler)
            except Exception as inner_e:
                logger.warning(f"asyncio安装问题: {inner_e}")
    except Exception as e:
        logger.warning(f"asyncio安装问题: {e}")


def _install_multiprocessing_handler():
    try:
        import multiprocessing
        original_get_context = multiprocessing.get_context
        def patched_get_context(method=None):
            ctx = original_get_context(method)
            original_run = ctx.Process.run
            @functools.wraps(original_run)
            def patched_run(self, *args, **kwargs):
                try:
                    original_run(self, *args, **kwargs)
                except Exception as e:
                    tb_str = _safe_call(lambda: _format_exception_chain(e), default=str(e))
                    ctx_str = _safe_call(_get_system_context)
                    print(f"[子进程问题] {type(e).__name__}: {e}\n{tb_str}\n\n[上下文]\n{ctx_str}", file=sys.stderr)
                    raise
            ctx.Process.run = patched_run
            return ctx
        multiprocessing.get_context = patched_get_context
    except Exception as e:
        logger.warning(f"multiprocessing 安装问题: {e}")


def _install_concurrent_futures_handler():
    _original_thread_submit = ThreadPoolExecutor.submit
    _original_process_submit = ProcessPoolExecutor.submit
    def patched_thread_submit(self, fn, *args, **kwargs):
        future = _original_thread_submit(self, fn, *args, **kwargs)
        def log_exception(f: Future):
            try: f.result()
            except Exception as e:
                tb_str = _safe_call(lambda: _format_exception_chain(e), default=str(e))
                logger.critical(f"[ThreadPool问题] {type(e).__name__}: {e}\n{tb_str}")
        future.add_done_callback(log_exception)
        return future
    ThreadPoolExecutor.submit = patched_thread_submit
    def patched_process_submit(self, fn, *args, **kwargs):
        future = _original_process_submit(self, fn, *args, **kwargs)
        def log_exception(f: Future):
            try: f.result()
            except Exception as e:
                tb_str = _safe_call(lambda: _format_exception_chain(e), default=str(e))
                logger.critical(f"[ProcessPool问题] {type(e).__name__}: {e}\n{tb_str}")
        future.add_done_callback(log_exception)
        return future
    ProcessPoolExecutor.submit = patched_process_submit


def _install_signal_handlers():
    def signal_handler(signum, frame):
        sig_name = signal.Signals(signum).name if hasattr(signal, 'Signals') else str(signum)
        ctx = _safe_call(_get_system_context)
        if signum in (signal.SIGINT, signal.SIGTERM):
            logger.info(f"[信号] 收到 {sig_name}，准备退出\n{ctx}")
            sys.exit(0)
        else:
            logger.critical(f"[信号] 收到 {sig_name}\n{ctx}")
            sys.exit(1)
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGABRT):
        try:
            _original_sig_handlers[sig] = signal.signal(sig, signal_handler)
        except Exception:
            pass


def _install_atexit_handler():
    def on_exit():
        logger.info(f"{APP_NAME} 退出 (PID: {os.getpid()})")
        global _faulthandler_file
        if _faulthandler_file:
            try: faulthandler.disable(); _faulthandler_file.close()
            except Exception: pass
        for sig, handler in _original_sig_handlers.items():
            if handler is not None:
                try: signal.signal(sig, handler)
                except Exception: pass
    atexit.register(on_exit)


def init_exhook():
    if logger._hooks_installed: return
    _install_faulthandler()
    _install_sys_excepthook()
    _install_threading_excepthook()
    _install_qt_message_handler()
    _install_asyncio_exception_handler()
    _install_multiprocessing_handler()
    _install_concurrent_futures_handler()
    _install_signal_handlers()
    _install_atexit_handler()
    logger._hooks_installed = True
