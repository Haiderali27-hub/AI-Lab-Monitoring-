import zipfile
import datetime

zip_path = r"d:\Roshaan\Downloads\fyp_doc_3chatp (2).zip"

with zipfile.ZipFile(zip_path, 'r') as z:
    for info in z.infolist():
        if "critical_path" in info.filename:
            dt = datetime.datetime(*info.date_time)
            print(f"File: {info.filename}")
            print(f"  Size: {info.file_size} bytes")
            print(f"  Modified: {dt}")
