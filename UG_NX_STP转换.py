#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""选择文件夹，使用 NX 自带的 STEP 批处理转换器重新导出其中的 STP 文件。"""

import os
import queue
import shutil
import subprocess
import tempfile
import threading
import traceback
from pathlib import Path
from tkinter import BOTH, DISABLED, END, LEFT, NORMAL, RIGHT, Button, Label, StringVar, Tk, filedialog, messagebox
from tkinter.scrolledtext import ScrolledText


NX_ROOTS = (
    Path(r"D:\Program Files\Siemens\DesigncenterNX2512"),
    Path(r"C:\Program Files\Siemens\DesigncenterNX2512"),
)


def find_nx_root():
    configured = os.environ.get("UGII_BASE_DIR")
    candidates = ([Path(configured)] if configured else []) + list(NX_ROOTS)
    for root in candidates:
        if (root / "STEP214UG" / "step214ug.cmd").is_file():
            return root
    raise FileNotFoundError("未找到 NX 的 STEP214UG\\step214ug.cmd，请确认已安装 NX 2512。")


def nx_environment(nx_root, work_dir):
    environment = os.environ.copy()
    nxbin = nx_root / "NXBIN"
    step_dir = nx_root / "STEP214UG"
    environment["UGII_BASE_DIR"] = str(nx_root)
    environment["UGII_ROOT_DIR"] = str(nxbin)
    environment["UGII_TEMPLATE_DIR"] = str(nx_root / "UGII" / "templates")
    environment["STEP214UG_DIR"] = str(step_dir) + os.sep
    environment["UGII_TMP_DIR"] = str(work_dir)
    license_file = Path(r"C:\ProgramData\Siemens\siemens_SSQ.dat")
    if not environment.get("SPLM_LICENSE_SERVER") and license_file.is_file():
        environment["SPLM_LICENSE_SERVER"] = str(license_file)
    environment["PATH"] = str(nxbin) + os.pathsep + str(step_dir) + os.pathsep + environment.get("PATH", "")
    return environment


def quote_cmd(value):
    return '"' + str(value).replace('"', '""') + '"'


def run_translator(cmd_file, source, output, definition, log_file, environment, work_dir):
    command = "call {cmd} {source} {output_arg} {log_arg} {definition_arg}".format(
        cmd=quote_cmd(cmd_file),
        source=quote_cmd(source),
        output_arg="o=" + quote_cmd(output),
        log_arg="l=" + quote_cmd(log_file),
        definition_arg="d=" + quote_cmd(definition),
    )
    result = subprocess.run(
        command,
        shell=True,
        executable=os.environ.get("COMSPEC", r"C:\Windows\System32\cmd.exe"),
        cwd=str(work_dir),
        env=environment,
        capture_output=True,
        text=True,
        encoding="mbcs",
        errors="replace",
    )
    # 部分 NX 翻译器会在已经生成文件后仍返回非零状态，因此以文件结果为准。
    if not output.is_file() or output.stat().st_size == 0:
        detail = (result.stderr or result.stdout or "NX 未返回错误文本。").strip()
        raise RuntimeError("NX 转换失败（退出码 {0}）：{1}".format(result.returncode, detail))


def stp_files(folder):
    for file in folder.rglob("*"):
        if not file.is_file() or file.suffix.lower() not in (".stp", ".step"):
            continue
        if any(parent.name.lower().endswith("_ug") for parent in file.parents):
            continue
        yield file


def convert_folder(source_folder, report):
    nx_root = find_nx_root()
    translator = nx_root / "STEP214UG" / "step214ug.cmd"
    step_dir = nx_root / "STEP214UG"
    import_definition = step_dir / "step214ug.def"
    export_definition = step_dir / "ugstep214.def"
    files = list(stp_files(source_folder))
    if not files:
        raise RuntimeError("所选文件夹及其子文件夹中没有 STP/STEP 文件。")

    log_path = source_folder / "UG导出日志.txt"
    success = 0
    failure = 0
    with log_path.open("w", encoding="utf-8") as report_file:
        for index, source in enumerate(files, 1):
            destination_dir = source.parent.with_name(source.parent.name + "_UG")
            destination = destination_dir / (source.stem + ".stp")
            import_log = destination_dir / ("." + source.stem + ".ugimport.log")
            export_log = destination_dir / ("." + source.stem + ".ugexport.log")
            report("[{0}/{1}] {2}".format(index, len(files), source.name))
            try:
                destination_dir.mkdir(parents=True, exist_ok=True)
                with tempfile.TemporaryDirectory(prefix="NX_STEP_") as temporary_dir:
                    work_dir = Path(temporary_dir)
                    temporary_source = work_dir / ("input" + source.suffix.lower())
                    temporary_part = work_dir / "imported.prt"
                    temporary_output = work_dir / "output.stp"
                    temporary_import_log = work_dir / "import.log"
                    temporary_export_log = work_dir / "export.log"
                    shutil.copy2(source, temporary_source)
                    environment = nx_environment(nx_root, work_dir)
                    run_translator(translator, temporary_source, temporary_part, import_definition, temporary_import_log, environment, work_dir)
                    run_translator(translator, temporary_part, temporary_output, export_definition, temporary_export_log, environment, work_dir)
                    shutil.copy2(temporary_output, destination)
                    if temporary_import_log.is_file():
                        shutil.copy2(temporary_import_log, import_log)
                    if temporary_export_log.is_file():
                        shutil.copy2(temporary_export_log, export_log)
                success += 1
                report_file.write("成功：{0}\n".format(source))
                report("  成功：{0}".format(destination))
            except Exception as error:
                failure += 1
                detail = "失败：{0}\n{1}".format(source, error)
                report_file.write(detail + "\n")
                report(detail)

    return success, failure, log_path


class Application:
    def __init__(self):
        self.window = Tk()
        self.window.title("UG NX 批量导出 STP")
        self.window.geometry("760x490")
        self.folder = StringVar()
        self.messages = queue.Queue()

        Label(self.window, text="源文件夹：").pack(anchor="w", padx=12, pady=(12, 0))
        Label(self.window, textvariable=self.folder, anchor="w", relief="sunken").pack(fill="x", padx=12, pady=4)
        self.choose_button = Button(self.window, text="选择文件夹", command=self.choose_folder)
        self.choose_button.pack(side=LEFT, padx=(12, 6), pady=6)
        self.start_button = Button(self.window, text="开始转换", command=self.start, state=DISABLED)
        self.start_button.pack(side=LEFT, pady=6)
        Button(self.window, text="关闭", command=self.window.destroy).pack(side=RIGHT, padx=12, pady=6)

        self.output = ScrolledText(self.window, state=DISABLED, wrap="word")
        self.output.pack(fill=BOTH, expand=True, padx=12, pady=(4, 12))
        self.window.after(100, self.drain_messages)

    def choose_folder(self):
        selected = filedialog.askdirectory(title="选择包含 STP 文件的文件夹")
        if selected:
            self.folder.set(selected)
            self.start_button.config(state=NORMAL)

    def write(self, text):
        self.output.config(state=NORMAL)
        self.output.insert(END, text + "\n")
        self.output.see(END)
        self.output.config(state=DISABLED)

    def start(self):
        source = Path(self.folder.get())
        if not source.is_dir():
            messagebox.showerror("无法转换", "请选择有效的文件夹。")
            return
        self.start_button.config(state=DISABLED)
        self.choose_button.config(state=DISABLED)
        self.write("开始转换：" + str(source))
        threading.Thread(target=self.worker, args=(source,), daemon=True).start()

    def worker(self, source):
        try:
            result = convert_folder(source, lambda text: self.messages.put(("text", text)))
            self.messages.put(("done", result))
        except Exception as error:
            self.messages.put(("error", "转换未开始：\n" + str(error) + "\n\n" + traceback.format_exc()))

    def drain_messages(self):
        try:
            while True:
                kind, value = self.messages.get_nowait()
                if kind == "text":
                    self.write(value)
                elif kind == "done":
                    success, failure, log_path = value
                    text = "完成：成功 {0}，失败 {1}。\n日志：{2}".format(success, failure, log_path)
                    self.write(text)
                    messagebox.showinfo("转换完成", text)
                    self.start_button.config(state=NORMAL)
                    self.choose_button.config(state=NORMAL)
                else:
                    self.write(value)
                    messagebox.showerror("转换失败", value)
                    self.start_button.config(state=NORMAL)
                    self.choose_button.config(state=NORMAL)
        except queue.Empty:
            pass
        self.window.after(100, self.drain_messages)

    def run(self):
        self.window.mainloop()


if __name__ == "__main__":
    Application().run()
