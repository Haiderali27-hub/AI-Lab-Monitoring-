import pypdf
import re

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    
    # Let's search for brackets containing numbers like [1], [12], [52], [52, 53] in pages 50 to 120 (0-indexed: 49 to 119)
    # which correspond to Chapter 4, 5, 6, 7 in the reference document.
    pattern = re.compile(r"\[\d+(?:,\s*\d+)*\]")
    
    print("SEARCHING FOR CITATIONS IN THE REFERENCE PDF:")
    for idx in range(49, 120):
        if idx >= len(reader.pages):
            break
        text = reader.pages[idx].extract_text()
        matches = list(pattern.finditer(text))
        if matches:
            print(f"\n--- Page {idx+1} ---")
            for m in matches:
                # Find the sentence containing the match
                start = max(0, m.start() - 60)
                end = min(len(text), m.end() + 60)
                snippet = text[start:end].replace("\n", " ")
                print(f"  Match '{m.group()}' context: ... {snippet} ...")

if __name__ == "__main__":
    main()
