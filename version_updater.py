import requests
import re
import urllib3
import os
import py7zr
import shutil
from logger import logger
from version import VERSION_URL, UPDATE_URL, CHANGELOG_URL

# 禁用 SSL 警告
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)


def get_changelog_from_github(max_retries=3):
    """从 GitHub 获取更新日志"""
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第 {attempt + 1} 次重试获取更新日志")
            
            logger.info(f"正在从 GitHub 获取更新日志：{CHANGELOG_URL}")
            response = requests.get(CHANGELOG_URL, timeout=10, verify=False)
            response.raise_for_status()
            
            content = response.text
            logger.info(f"成功获取更新日志，长度：{len(content)}")
            return content
            
        except requests.exceptions.Timeout:
            logger.warning(f"请求超时（尝试 {attempt + 1}/{max_retries}）")
            if attempt == max_retries - 1:
                logger.error("获取更新日志失败：多次重试后仍超时")
                return None
        except requests.exceptions.RequestException as e:
            logger.error(f"获取更新日志失败：{str(e)}")
            return None
        except Exception as e:
            logger.error(f"获取更新日志时出错：{str(e)}")
            return None
    
    return None


def check_version_from_github(max_retries=3):
    """从 GitHub 获取版本信息 """
    result = {
        'success': False,
        'version': None,
        'build_date': None,
        'update_url': UPDATE_URL,
        'changelog': None,
        'error': None
    }
    
    try:
        logger.info(f"正在从 GitHub 获取版本信息：{VERSION_URL}")
        response = requests.get(VERSION_URL, timeout=10, verify=False)
        response.raise_for_status()
        
        content = response.text
        
        version_match = re.search(r'VERSION\s*=\s*["\']([^"\']+)["\']', content)
        build_date_match = re.search(r'BUILD_DATE\s*=\s*["\']([^"\']+)["\']', content)
        
        if version_match and build_date_match:
            result['version'] = version_match.group(1)
            result['build_date'] = build_date_match.group(1)
            result['success'] = True
            logger.info(f"成功获取版本信息：{result['version']} ({result['build_date']})")
            
            changelog = get_changelog_from_github()
            if changelog:
                result['changelog'] = changelog
            else:
                result['changelog'] = f"# 版本 {result['version']}\n构建日期：{result['build_date']}\n\n暂无详细更新日志"
        else:
            result['error'] = "无法解析版本信息"
            logger.error(result['error'])
            
    except requests.exceptions.Timeout:
        result['error'] = "请求超时"
        logger.error(result['error'])
    except requests.exceptions.RequestException as e:
        result['error'] = f"网络错误：{str(e)}"
        logger.error(result['error'])
    except Exception as e:
        result['error'] = f"解析错误：{str(e)}"
        logger.error(result['error'])
    
    return result


def download_update(download_path, progress_callback=None, max_retries=3):
    """下载更新文件"""
    url = UPDATE_URL
    
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第 {attempt + 1} 次重试下载更新")
            
            logger.info(f"正在下载更新：{url}")
            response = requests.get(url, stream=True, timeout=30, verify=False)
            response.raise_for_status()
            
            total_size = int(response.headers.get('content-length', 0))
            downloaded_size = 0
            
            with open(download_path, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    if chunk:
                        f.write(chunk)
                        downloaded_size += len(chunk)
                        if progress_callback and total_size > 0:
                            progress_callback(downloaded_size, total_size)
            
            logger.info(f"更新下载完成：{download_path}")
            return True
            
        except requests.exceptions.Timeout:
            logger.warning(f"下载超时（尝试 {attempt + 1}/{max_retries}）")
            if attempt == max_retries - 1:
                logger.error("下载更新失败：多次重试后仍超时")
                return False
        except requests.exceptions.RequestException as e:
            logger.error(f"下载更新失败：{str(e)}")
            return False
        except Exception as e:
            logger.error(f"下载更新时出错：{str(e)}")
            return False
    
    return False


def extract_update(archive_path, extract_folder):
    """解压更新文件"""
    try:
        logger.info(f"正在解压更新：{archive_path} -> {extract_folder}")
        
        if os.path.exists(extract_folder):
            shutil.rmtree(extract_folder)
        os.makedirs(extract_folder)
        
        if archive_path.endswith('.7z'):
            with py7zr.SevenZipFile(archive_path, 'r') as archive:
                archive.extractall(extract_folder)
        elif archive_path.endswith('.zip'):
            import zipfile
            with zipfile.ZipFile(archive_path, 'r') as zip_ref:
                zip_ref.extractall(extract_folder)
        else:
            logger.error(f"不支持的压缩格式：{archive_path}")
            return False
        
        logger.info(f"更新解压完成：{extract_folder}")
        return True
        
    except Exception as e:
        logger.error(f"解压更新失败：{str(e)}")
        return False


def create_update_script(app_dir, update_folder, script_path=None):
    """创建BAT"""
    if script_path is None:
        script_path = os.path.join(update_folder, 'update.bat')
    
    bat_content = f'''@echo off
chcp 65001 >nul
echo   ClassLively
echo.

echo [1/4] 等待进程关闭
timeout /t 3 /nobreak >nul

echo [2/4] 复制更新文件
xcopy /E /Y /I /H "{update_folder}\\*" "{app_dir}"
if errorlevel 1 (
    echo 文件复制失败！
    pause
    exit /b 1
)

echo [3/4] 清理临时文件
timeout /t 1 /nobreak >nul
rmdir /S /Q "{update_folder}"

echo [4/4] 启动 ClassLively
timeout /t 1 /nobreak >nul
start "" "{app_dir}\\ClassLively.exe"

echo.
echo   更新完成
timeout /t 2 /nobreak >nul
exit
'''
    
    try:
        with open(script_path, 'w', encoding='utf-8') as f:
            f.write(bat_content)
        
        logger.info(f"更新脚本已创建：{script_path}")
        return script_path
        
    except Exception as e:
        logger.error(f"创建更新脚本失败：{str(e)}")
        return None


def apply_update(update_folder, app_dir):
    """复制文件到目录"""
    try:
        logger.info(f"正在应用更新：{update_folder} -> {app_dir}")
        
        for root, dirs, files in os.walk(update_folder):
            relative_path = os.path.relpath(root, update_folder)
            target_dir = os.path.join(app_dir, relative_path)
            
            if not os.path.exists(target_dir):
                os.makedirs(target_dir)
            
            for file in files:
                if file == 'update.bat':
                    continue
                
                src_file = os.path.join(root, file)
                dst_file = os.path.join(target_dir, file)
                
                try:
                    if os.path.exists(dst_file):
                        os.remove(dst_file)
                    shutil.copy2(src_file, dst_file)
                    logger.info(f"已更新文件：{file}")
                except Exception as e:
                    logger.error(f"复制文件 {file} 失败：{str(e)}")
                    return False
        
        logger.info("更新应用完成")
        return True
        
    except Exception as e:
        logger.error(f"应用更新失败：{str(e)}")
        return False


if __name__ == "__main__":
    # 测试
    version, build_date = get_version_from_github()
    if version and build_date:
        print(f"最新版本：{version}")
        print(f"构建日期：{build_date}")
    else:
        print("获取版本信息失败")
