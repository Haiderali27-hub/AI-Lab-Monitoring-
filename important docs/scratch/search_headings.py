import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    
    # We want to check pages 10 to 20 (0-indexed: 9 to 19)
    print("Searching for headings in reference_fyp_dpc.pdf:")
    for idx in range(9, 22):
        text = reader.pages[idx].extract_text()
        for line in text.split("\n"):
            if "1.5" in line or "Proposed Solution" in line:
                print(f"Page {idx+1}: {line.strip()}")

if __name__ == "__main__":
    main()
