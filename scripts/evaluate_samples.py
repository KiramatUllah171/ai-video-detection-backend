import argparse
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate local real/AI samples against the AI inference service.")
    parser.add_argument("--root", default="sample_data", help="Sample root containing real/ and ai/ folders.")
    parser.add_argument("--url", default="http://localhost:8000/analyze-frames", help="AI service analyze endpoint.")
    parser.add_argument("--frames", type=int, default=16, help="Number of frames to sample per video/folder.")
    parser.add_argument("--out", default="calibration_reports", help="Directory for JSON/CSV calibration reports.")
    args = parser.parse_args()

    backend_root = Path(__file__).resolve().parents[1]
    ai_service_script = backend_root.parent / "ai-video-detection-ai-service" / "scripts" / "evaluate_samples.py"
    if not ai_service_script.exists():
        print(f"AI service evaluator not found: {ai_service_script}", file=sys.stderr)
        return 2

    command = [
        sys.executable,
        str(ai_service_script),
        "--root",
        str((backend_root / args.root).resolve()),
        "--url",
        args.url,
        "--frames",
        str(args.frames),
        "--out",
        str((backend_root / args.out).resolve()),
    ]
    return subprocess.call(command)


if __name__ == "__main__":
    raise SystemExit(main())
