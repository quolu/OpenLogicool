"""保存済みの観測ログから監視間隔と終盤のHPを描画する。"""
import json
import argparse
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
plt.rcParams["font.family"] = "Meiryo"

root = Path(__file__).parent
parser = argparse.ArgumentParser(description="回復監視の実測ログを描画する")
parser.add_argument("--events", default="recovery-cadence-events.jsonl")
parser.add_argument("--output", default="recovery-cadence.png")
parser.add_argument("--threshold", type=float, default=50)
parser.add_argument("--start-ms", type=int, default=100000)
args = parser.parse_args()
events = [json.loads(line) for line in (root / args.events).read_text(encoding="utf-8-sig").splitlines()]
samples = [e for e in events if e["Event"] == "recovery-sample" and e["IntervalMs"] is not None]
health = [e for e in events if e["Event"] == "recovery" and e["Observation"]["HealthFraction"] is not None and e["AtMs"] >= args.start_ms]
fig, axes = plt.subplots(2, 1, figsize=(10, 6), layout="constrained")
axes[0].plot([e["AtMs"] / 1000 for e in samples], [e["IntervalMs"] for e in samples], linewidth=1)
axes[0].axhline(250, color="gray", linestyle="--", label="目標250ms")
axes[0].set(xlabel="経過時間（秒）", ylabel="観測間隔（ms）", title="実戦中の回復監視と終盤のHP")
axes[0].set_ylim(0, 400)
axes[0].legend()
axes[1].plot([e["AtMs"] / 1000 for e in health], [e["Observation"]["HealthFraction"] * 100 for e in health], color="#228844", label="HP残量")
axes[1].plot([e["AtMs"] / 1000 for e in health], [e["Observation"]["WhiteFraction"] * 100 for e in health], color="#888888", label="白い部分")
axes[1].axhline(args.threshold, color="#228844", linestyle=":")
axes[1].axhline(20, color="#888888", linestyle=":")
for e in events:
    if e["Event"] == "recovery-input" and e["Action"] in ("Potion", "Bandage"):
        x = e["AtMs"] / 1000
        axes[1].axvline(x, color="#cc7733", linestyle="--")
        label = "包帯" if e["Action"] == "Bandage" else "ポーション"
        axes[1].text(x, 90 if e["Action"] == "Bandage" else 76, f'{label}\n{e["DetectionToDispatchMs"]}ms', fontsize=8)
axes[1].set(xlabel="経過時間（秒）", ylabel="HPバー全長に対する割合（％）", ylim=(0, 100))
axes[1].legend(loc="lower left")
for ax in axes:
    ax.grid(alpha=0.2)
fig.savefig(root / args.output, dpi=150)
