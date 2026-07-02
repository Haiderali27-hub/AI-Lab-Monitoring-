import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp first submitted doc .pdf"
    reader = pypdf.PdfReader(pdf_path)
    for idx in range(len(reader.pages)):
        text = reader.pages[idx].extract_text()
        for line in text.split("\n"):
            line = line.strip()
            if line and line[0].isdigit():
                print(f"Page {idx+1}: {line}")

if __name__ == "__main__":
    main()
