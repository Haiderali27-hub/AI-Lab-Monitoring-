import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    print(f"Total pages: {len(reader.pages)}")
    
    for idx, page in enumerate(reader.pages):
        text = page.extract_text()
        if "SYSTEM INTERFACE AND PHYSICAL DESIGN" in text or "SYSTEM INTERFACE & PHYSICAL DESIGN" in text:
            print(f"Match found on PDF Page {idx+1}")

if __name__ == "__main__":
    main()
