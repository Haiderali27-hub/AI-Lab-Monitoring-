import urllib.request
import os

def download_image(url, dest_path):
    try:
        req = urllib.request.Request(
            url, 
            headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'}
        )
        with urllib.request.urlopen(req) as response:
            with open(dest_path, 'wb') as out_file:
                out_file.write(response.read())
        print(f"Successfully downloaded: {os.path.basename(dest_path)}")
    except Exception as e:
        print(f"Error downloading {os.path.basename(dest_path)}: {e}")

def main():
    dest_dir = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp_doc_3chatp\Figures"
    
    # 1. Labs and Workstations Seating Maps
    url_labs = "https://lh3.googleusercontent.com/aida/AP1WRLslnx2EaWxNZyVggIhRvh9hU-bFud4Go-svSbE8ZCZGHDXo-fi5Kvk41usj4Of0CWtUrMHDu83IWs1LD4FfuUaZ_VwBfSsKWHGvlHjp3F4O2ArrisZzaXXoypccD_amJdCtCrEE2OkVk8ySCSPie69EimJLEK8TkRNKESdmmYQwePEAocXgKwtgzXZm4aS-xN_924YhOqBRYfvLIKDVbCDvuZGSzdWgg_4VrRnR0iUyomLFOW-KoMiP1Q"
    download_image(url_labs, os.path.join(dest_dir, "ui_labs_workstations.png"))
    
    # 2. Device Bindings
    url_bindings = "https://lh3.googleusercontent.com/aida/AP1WRLuYnFND247P9OrLv02cUr19WtBTIfTkhp-j3VTQ3jASyylsoSPqpFoI2o4RdUFLX-9yBQhnSXEK1YSxK1btr0UGqICQ3IfOS7imeO7pjBJuhaJSVEmpoqvgeaXRzK6Kyk2LhgdwSvrVCtameYf_fD9-alLqYKPkzN5emWiKcszTB8C1JaC1CdI70KUxsDkYbRnGOPATWpaJqi2AWH0KusfqXedqRdurtlp5qgRpm0kZsiPkFICpvYB82Q"
    download_image(url_bindings, os.path.join(dest_dir, "ui_device_bindings.png"))
    
    # 3. Student Performance Analytics
    url_performance = "https://lh3.googleusercontent.com/aida/AP1WRLvTujZn-FPUl621MZ9ArbE_cCL1T973M1kKHS7j4X3ibQI_Hm7y98Jmjjfo6rlieFJazqPzu6FqWg-7jYqgc-2MOgUD0vHeypkxgk2oZbV0rdTrz7zG_lEkFS2_E68zLIUJp6pjYG1EKtorYOYpLklqfzL11_4n96IDVF1Cs80oJF0OQoiDy1fviFNL-Kkg2N-NmF2MtX1jHmD6GAsHgKSXplJGKwl3A0XEiYjSuax5Sft6iLe-iSm2HUk"
    download_image(url_performance, os.path.join(dest_dir, "ui_student_performance.png"))
    
    # 4. Security Audit Logs
    url_audit = "https://lh3.googleusercontent.com/aida/AP1WRLvwQnsISGsJopBYIRnH2oq49S1ONYdvrs61-4iie4IIaVv3QGZpJHIYaUYMSdfjPt9GDzw_0xDGud0zGI7S1a2lA-C7sG_45NE8YRejZAQLjfFHMHTY9Acf9Q8wrYFMvl3eHa6YLQW1qeBLTm7vz1CnoGInV3C85btZyn9b4tiJsd6SpvbHlIZIHQfU8GQxBxz6rEA6JCVWNIeY3Bv3W15bzoKBHTVz6QbQCITXhKTUNvRe1pG5pi_CHgI"
    download_image(url_audit, os.path.join(dest_dir, "ui_audit_logs.png"))

if __name__ == "__main__":
    main()
