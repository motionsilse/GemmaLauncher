"""Prepare a historical WPF test harness without changing historical product code."""
import argparse
import io
from pathlib import Path
import subprocess
import zipfile


def prepare(revision: str, output: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    output = (root / output).resolve()
    if not output.is_relative_to(root / "artifacts") or output.exists():
        raise ValueError("Use a new directory beneath artifacts; existing data is never overwritten.")
    archive = subprocess.run(["git", "archive", "--format=zip", revision], cwd=root,
                             check=True, capture_output=True).stdout
    output.mkdir(parents=True)
    with zipfile.ZipFile(io.BytesIO(archive)) as source:
        for item in source.infolist():
            if not (output / item.filename).resolve().is_relative_to(output):
                raise ValueError("Archive path is outside the baseline directory.")
        source.extractall(output)
    program = output / "tests/GemmaLauncher.UiTests/Program.cs"
    text = program.read_text(encoding="utf-8-sig")

    def replace(old: str, new: str) -> None:
        nonlocal text
        if text.count(old) != 1:
            raise ValueError(f"Unsupported historical harness; expected one occurrence of {old!r}")
        text = text.replace(old, new)

    replace("public static class Program", "public static partial class Program")
    replace("        try { return Run(args); }",
            '        if (args.Contains("--metrics-child")) { Thread.Sleep(180000); return 0; }\n'
            '        try { return Run(args); }')
    replace('                if (args.Contains("--render-library"))',
            '                if (args.Length == 2 && args[0] == "--benchmark") { await BenchmarkAsync(args[1]); return; }\n'
            '                if (args.Length == 2 && args[0] == "--graphics-probe") { await GraphicsProbeAsync(args[1]); return; }\n'
            '                if (args.Contains("--render-library"))')
    replace('private void Set(ServerState state) { Status = new(state, state.ToString());',
            'public int? ProcessId { get; set; }\n'
            '        private void Set(ServerState state) { Status = new(state, state.ToString(), ProcessId);')
    program.write_text(text, encoding="utf-8")
    project = program.with_name("GemmaLauncher.UiTests.csproj")
    text = project.read_text(encoding="utf-8-sig")
    if "<UseWPF>true</UseWPF>" not in text:
        raise ValueError("The requested baseline is not a WPF harness.")
    project.write_text(text.replace("<UseWPF>", "<DefineConstants>$(DefineConstants);WPF_BASELINE</DefineConstants><UseWPF>", 1), encoding="utf-8")
    program.with_name("Benchmark.cs").write_bytes((root / "tests/GemmaLauncher.UiTests/Benchmark.cs").read_bytes())
    print(f"Baseline prepared at {output}. Only the UI test harness was instrumented.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", default="f6f0c5409fc21e200881b8d2906233aac12e3e0c")
    parser.add_argument("--output", type=Path, default=Path("artifacts/lightweight/baseline-reference"))
    arguments = parser.parse_args()
    prepare(arguments.ref, arguments.output)
