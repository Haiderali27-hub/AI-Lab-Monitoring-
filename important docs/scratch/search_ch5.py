import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    print(f"Total pages: {len(reader.pages)}")
    for idx, page in enumerate(reader.pages):
        text = page.extract_text()
        if "SYSTEM DESIGN" in text:
            print(f"Found 'SYSTEM DESIGN' on PDF Page {idx+1}")
        if "SYSTEM INTERFACE" in text:
            print(f"Found 'SYSTEM INTERFACE' on PDF Page {idx+1}")

if __name__ == "__main__":
    main()
