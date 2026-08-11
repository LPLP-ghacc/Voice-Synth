Следует выполнить загрузку пакета Piper из репозитория GitHub releases:
https://github.com/rhasspy/piper/releases/latest (https://github.com/rhasspy/piper/releases/latest)
Файл piper_windows_amd64.zip необходимо извлечь, взяв содержимое директории piper\

Следует загрузить модель речевого синтеза:
https://huggingface.co/rhasspy/piper-voices/[путь до голоса].onnx
https://huggingface.co/rhasspy/piper-voices/[путь до голоса].onnx.json
Требуется наличие обоих файлов: .onnx и .onnx.json (конфигурационный файл модели).

Все файлы должны быть размещены в директории:
[корень программы]\piper\

Итоговая структура каталога должна соответствовать следующему виду:
piper\
  piper.exe
  espeak-ng.dll
  onnxruntime.dll
  onnxruntime_providers_shared.dll
  piper_phonemize.dll
  [голос].onnx
  [голос].onnx.json
  espeak-ng-data\        ← каталог с лингвистическими данными

После завершения установки необходимо выбрать в конфигурации приложения: Piper TTS → голос [голос] → сохранить.