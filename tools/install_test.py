# -*- coding: utf-8 -*-
"""端到端验证：安装 -> 检查 -> 卸载 -> 检查清理。"""
import ctypes, os, subprocess, sys, time, winreg
from ctypes import wintypes

u = ctypes.WinDLL('user32', use_last_error=True)
EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
BM_CLICK = 0x00F5
WM_COMMAND = 0x0111
IDOK = 1

SETUP = sys.argv[1]
INSTALL_DIR = os.path.join(os.environ['LOCALAPPDATA'], 'Programs', 'EyeSprite')
DESKTOP_LNK = os.path.join(os.environ['USERPROFILE'], 'Desktop', '护眼精灵.lnk')
MENU_LNK = os.path.join(os.environ['APPDATA'], r'Microsoft\Windows\Start Menu\Programs', '护眼精灵.lnk')


def win_text(hwnd):
    buf = ctypes.create_unicode_buffer(512)
    u.GetWindowTextW(hwnd, buf, 512)
    return buf.value


def win_class(hwnd):
    buf = ctypes.create_unicode_buffer(256)
    u.GetClassNameW(hwnd, buf, 256)
    return buf.value


def top_windows(pid):
    out = []

    def cb(hwnd, lparam):
        p = wintypes.DWORD()
        u.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid:
            out.append((hwnd, win_text(hwnd), win_class(hwnd)))
        return True

    u.EnumWindows(EnumWindowsProc(cb), 0)
    return out


def wait_top(pid, keyword, timeout=30):
    end = time.time() + timeout
    while time.time() < end:
        for hwnd, t, c in top_windows(pid):
            if keyword in t:
                return hwnd, t
        time.sleep(0.3)
    return None, None


def children(hwnd):
    out = []

    def cb(h, lparam):
        out.append((h, win_text(h), win_class(h)))
        return True

    u.EnumChildWindows(hwnd, EnumWindowsProc(cb), 0)
    return out


def click_button(dlg, labels):
    for h, t, c in children(dlg):
        if 'button' in c.lower() and t.strip() in labels:
            print('  click button:', t, hex(h))
            u.PostMessageW(h, BM_CLICK, 0, 0)
            return True
    print('  !! button not found, children =', children(dlg))
    return False


print('=== 安装 ===')
p = subprocess.Popen([SETUP])
print('pid =', p.pid)
hwnd, title = wait_top(p.pid, '安装程序')
print('main window:', hwnd, title)
time.sleep(1.0)
click_button(hwnd, {'安装', '安装(I)', '立即安装'})
time.sleep(2.0)
# 安装完成的提示框
dlg, dt = wait_top(p.pid, '安装完成', timeout=60)
print('done dialog:', dlg, dt)
if dlg:
    click_button(dlg, {'确定', '完成'})
    u.PostMessageW(dlg, WM_COMMAND, IDOK, 0)
try:
    p.wait(timeout=40)
except Exception:
    pass
time.sleep(1.5)

print('=== 安装结果 ===')
print('exe:', os.path.exists(os.path.join(INSTALL_DIR, 'EyeSprite.exe')))
print('uninstaller:', os.path.exists(os.path.join(INSTALL_DIR, 'Uninstall.exe')))
if os.path.isdir(INSTALL_DIR):
    for n in sorted(os.listdir(INSTALL_DIR)):
        print('  %-16s %d' % (n, os.path.getsize(os.path.join(INSTALL_DIR, n))))
tl = subprocess.run(['tasklist', '/fi', 'imagename eq EyeSprite.exe'], capture_output=True,
                    text=True, encoding='mbcs', errors='replace')
print('app running:', 'EyeSprite.exe' in (tl.stdout or ''))
print('desktop lnk:', os.path.exists(DESKTOP_LNK))
print('startmenu lnk:', os.path.exists(MENU_LNK))
try:
    k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Uninstall\EyeSprite')
    print('DisplayName:', winreg.QueryValueEx(k, 'DisplayName')[0])
    print('DisplayVersion:', winreg.QueryValueEx(k, 'DisplayVersion')[0])
    print('UninstallString:', winreg.QueryValueEx(k, 'UninstallString')[0])
    winreg.CloseKey(k)
except FileNotFoundError:
    print('reg: MISSING')

def find_any(keyword, timeout=25):
    """全局按标题找可见窗口（卸载器会自我复制到临时目录，进程会变）"""
    end = time.time() + timeout
    while time.time() < end:
        found = []

        def cb(hwnd, lparam):
            if u.IsWindowVisible(hwnd):
                t = win_text(hwnd)
                if keyword in t:
                    found.append((hwnd, t))
            return True

        u.EnumWindows(EnumWindowsProc(cb), 0)
        if found:
            return found[0]
        time.sleep(0.3)
    return None, None


print('=== 卸载 ===')
un = os.path.join(INSTALL_DIR, 'Uninstall.exe')
p2 = subprocess.Popen([un])
hwnd, title = find_any('卸载护眼精灵', timeout=25)
print('confirm dialog:', hwnd, title)
if hwnd:
    u.PostMessageW(hwnd, WM_COMMAND, 6, 0)  # IDYES
time.sleep(1.2)
dlg, dt = find_any('卸载完成', timeout=30)
print('done dialog:', dlg, dt)
if dlg:
    u.PostMessageW(dlg, WM_COMMAND, IDOK, 0)
try:
    p2.wait(timeout=20)
except Exception:
    pass

print('=== 清理结果 ===')
for i in range(15):
    if not os.path.isdir(INSTALL_DIR):
        break
    time.sleep(1)
print('install dir exists:', os.path.isdir(INSTALL_DIR))
if os.path.isdir(INSTALL_DIR):
    print('leftover:', os.listdir(INSTALL_DIR))
print('desktop lnk:', os.path.exists(DESKTOP_LNK))
print('startmenu lnk:', os.path.exists(MENU_LNK))
try:
    k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Uninstall\EyeSprite')
    print('reg: STILL EXISTS')
    winreg.CloseKey(k)
except FileNotFoundError:
    print('reg: removed')
