import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp first submitted doc .pdf"
    reader = pypdf.PdfReader(pdf_path)
    
    print("Searching for headings in fyp first submitted doc .pdf:")
    for idx in range(len(reader.pages)):
        text = reader.pages[idx].extract_text()
        for line in text.split("\n"):
            if "1.5" in line or "Benefits" in line:
                print(f"Page {idx+1}: {line.strip()}")

if __name__ == "__main__":
    main()
