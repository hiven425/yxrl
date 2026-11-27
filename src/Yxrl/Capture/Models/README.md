# 中文截图 OCR 资源

识别权重和匹配字符表取自 RapidAI 的 RapidOCR v3.10.0 模型仓库，源模型由 PaddleOCR 发布，采用 Apache-2.0 许可。检测模型和文本行方向分类模型从锁定的 RapidOcrNet 4.2.0 包内嵌入。发布程序首次识别时将资源释放到当前用户的 Yxrl/ocr 目录。

- [中英文识别权重](https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx)
- [PP-OCRv5 字符表](https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt)
- [PaddleOCR 许可](LICENSE-PaddleOCR.txt)

高级识别模型由各自的公开模型仓库按需分发。项目代码锁定模型文件版本和 SHA-256。

- 文档识别使用 RapidLayout 的 CDLA 版面模型，缓存版本为 `layout-cdla-1.2.0`。[RapidLayout 与许可](https://github.com/RapidAI/RapidLayout)
- 表格识别使用 Paddle SLANet_plus，缓存版本为 `slanet-plus-2.0.0`。[RapidTable 与许可](https://github.com/RapidAI/RapidTable)
- 公式识别使用 Pix2Text-MFR 1.5 的 ONNX 编码器、解码器和分词器。[Pix2Text 模型卡与 MIT 许可](https://huggingface.co/breezedeus/pix2text-mfr-1.5)
