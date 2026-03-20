import requests
import re
import urllib3
from logger import logger

# 禁用 SSL 警告
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)


def get_changelog_from_github(max_retries=3):
    """从 GitHub 获取更新日志"""
    url = "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/changelog.md"
    
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第 {attempt + 1} 次重试获取更新日志")
            
            logger.info(f"正在从 GitHub 获取更新日志：{url}")
            response = requests.get(url, timeout=5, verify=False)
            response.raise_for_status()
            
            content = response.text
            logger.info(f"成功获取更新日志，长度：{len(content)}")
            return content
            
        except requests.exceptions.Timeout:
            logger.warning(f"请求超时（尝试 {attempt + 1}/{max_retries}）")
            if attempt == max_retries - 1:
                logger.error(f"获取更新日志失败：多次重试后仍超时")
                return None
        except requests.exceptions.HTTPError as e:
            if e.response.status_code == 404:
                logger.error(f"获取更新日志失败：文件不存在 (404)")
                return None
            logger.error(f"获取更新日志失败：{str(e)}")
            return None
        except requests.exceptions.RequestException as e:
            logger.error(f"获取更新日志失败：{str(e)}")
            return None
        except Exception as e:
            logger.error(f"获取更新日志时出错：{str(e)}")
            return None
    
    return None


def get_version_from_github(max_retries=3):
    url = "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/version.py"
    
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第 {attempt + 1} 次重试获取版本信息")
            
            logger.info(f"正在从 GitHub 获取版本信息：{url}")
            response = requests.get(url, timeout=5, verify=False)
            response.raise_for_status()
            
            content = response.text
            
            # 正则表达式 提取 版本号 版本信息
            version_match = re.search(r'VERSION\s*=\s*["\']([^"\']+)["\']', content)
            build_date_match = re.search(r'BUILD_DATE\s*=\s*["\']([^"\']+)["\']', content)
            
            if version_match and build_date_match:
                version = version_match.group(1)
                build_date = build_date_match.group(1)
                logger.info(f"成功获取版本信息：{version} ({build_date})")
                return version, build_date
            else:
                logger.error("无法解析版本信息")
                return None, None
                
        except requests.exceptions.Timeout:
            logger.warning(f"请求超时（尝试 {attempt + 1}/{max_retries}）")
            if attempt == max_retries - 1:
                logger.error(f"获取版本信息失败：多次重试后仍超时")
                return None, None
        except requests.exceptions.RequestException as e:
            logger.error(f"获取版本信息失败：{str(e)}")
            return None, None
        except Exception as e:
            logger.error(f"解析版本信息时出错：{str(e)}")
            return None, None
    
    return None, None


if __name__ == "__main__":
    # 测试
    version, build_date = get_version_from_github()
    if version and build_date:
        print(f"最新版本：{version}")
        print(f"构建日期：{build_date}")
    else:
        print("获取版本信息失败")
