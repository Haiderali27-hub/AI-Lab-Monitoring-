import pypdf
import re

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    print(f"Total pages: {len(reader.pages)}")
    
    # Search for headings like CHAPTER 1, CHAPTER 2, CHAPTER 3, CHAPTER 4, CHAPTER 5, CHAPTER 6, CHAPTER 7
    for idx, page in enumerate(reader.pages):
        text = page.extract_text()
        for line in text.split("\n"):
            line_upper = line.upper()
            if "CHAPTER" in line_upper or "SYSTEM INTERFACE" in line_upper:
                # print matching lines with page numbers
                print(f"Page {idx+1}: {line.strip()}")

if __name__ == "__main__":
    main()
