# t04 G13/G600 fast path起動

- `Macro:` tokenは既存`MappingProfile`のbindingへ単独で格納し、button downで一回だけ起動要求へ変換する。button up・stop releaseのownershipは作らない。
- `FastPathPump`はmacro edgeを物理outputから分離し、AI／capture／SQLite／UIへ到達しない`TryEnqueue`だけを呼ぶ。通常key／mouse／有限sequenceは従来のemitter経路を維持する。
- process内queueは有界FIFO。満杯／利用不能は通常inputをfault停止させず、拒否数と理由を観測可能にする。
- focused test: `DeviceMappingRuntimeTests|FastPathPumpTests` 30件 green（2026-08-26）。
