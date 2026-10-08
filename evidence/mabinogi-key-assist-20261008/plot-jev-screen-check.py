"""保存済みJev分類3件を追加API呼出しなしで図示する。Pillowを使用。"""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).parent
result = json.loads((root / 'jev-screen-check.json').read_text(encoding='utf-8'))
answers = result['response']['answers']
font_path = 'C:/Windows/Fonts/meiryo.ttc'
font = ImageFont.truetype(font_path, 17)
small = ImageFont.truetype(font_path, 14)
image = Image.new('RGB', (920, 330), 'white')
draw = ImageDraw.Draw(image)
draw.text((24, 14), f"Jevの文字分類：3件まとめて{result['elapsedMs']}ms・ゲーム入力0回", font=font, fill='#222222')
draw.text((24, 47), '候補ごとの確率（分類の正しさを保証する値ではない）', font=small, fill='#555555')
keys = [('dialogue', '会話', '#338866'), ('reward', '報酬', '#5599bb'), ('selection', '選択画面', '#bb8844'), ('unknown', '不明', '#888888')]
for row, (case, label) in enumerate([('dialogue', '会話のOCR'), ('bonus', 'ボーナスのOCR'), ('garbled', '崩れたOCR')]):
    y = 92 + row * 58
    draw.text((24, y), label, font=font, fill='#222222')
    x = 190
    for key, _, color in keys:
        value = answers[case]['probabilities'][key]
        right = x + value * 680
        draw.rectangle((x, y, right, y + 32), fill=color)
        if value >= 0.1:
            draw.text(((x + right) / 2 - 18, y + 4), f'{value:.2f}', font=small, fill='white')
        x = right
for index, (_, label, color) in enumerate(keys):
    x = 185 + index * 165
    draw.rectangle((x, 285, x + 20, 305), fill=color)
    draw.text((x + 29, 282), label, font=small, fill='#222222')
image.save(root / 'jev-screen-check.png')
