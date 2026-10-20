# On-device webcam models (Module 5, stage S8)

Both models run on the CPU through OpenCV's DNN module (OpenCvSharp4 + OpenCvSharp4.runtime.win), only while an exam
that requires a webcam is running, at about 1.5 frames per second. Nothing leaves the PC except the debounced events
(`NoFace`, `MultipleFaces`, `LookingAway`, `PhoneDetected`) and the webcam snapshot taken when one is raised.

The models are optional. If a file is missing, cannot be read, or keeps failing, only its check is switched off; the
exam and the camera-tamper checks keep working. The checks can also be turned off in `appsettings.desktop.json`
(`WebcamFaceDetection`, `WebcamPhoneDetection`).

| File | What it does | Source (downloaded unchanged) | SHA-256 | Licence |
|---|---|---|---|---|
| `face_detection_yunet_2023mar.onnx` (227 KB) | Face boxes, 5 landmarks, confidence | OpenCV Zoo, `models/face_detection_yunet/` — https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx | `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4` | MIT, © 2020 Shiqi Yu — `LICENSE-YuNet.txt` |
| `yolox_nano.onnx` (3.5 MB) | 80 COCO objects; only class 67 "cell phone" is used | Megvii YOLOX official release 0.1.1rc0 — https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx | `c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d` | Apache-2.0, © 2021-2022 Megvii Inc. — `LICENSE-YOLOX.txt` |

Ultralytics YOLOv8 weights (`yolov8n.pt`) are deliberately **not** used: they are AGPL-3.0, and a `.pt` file is a
Python pickle that can run code when it is loaded.

The decoding code (`Core/Monitoring/DetectionDecoders.cs`) and the rules (`Core/Monitoring/VisionIssueDetector.cs`,
`Core/Monitoring/FaceGeometry.cs`) are our own. They are unit-tested with synthetic tensors and landmarks
(M5-U-18 … M5-U-21).

To verify a file: `certutil -hashfile <file> SHA256` (Windows) or `sha256sum <file>`.
