import pandas as pd
import requests
import time
from pathlib import Path
from xml.sax.saxutils import escape

# -------- CONFIG --------
INPUT_FILE = "Colour Code.csv"
OUTPUT_FILE = "Proper_Road_Routes_with_colors.kml"
OSRM_BASE_URL = "http://router.project-osrm.org"
REQUEST_TIMEOUT = 30
SLEEP_BETWEEN_REQUESTS = 1

# KML color format = aabbggrr
COLOR_MAP = {
    "Car": "ff0000ff",      # Red
    "Bike": "ffff0000",     # Blue
    "Walking": "ff00ff00"   # Green
}

PROFILE_MAP = {
    "Car": "driving",
    "Bike": "cycling",
    "Walking": "foot"
}
# ------------------------


def detect_route_type_column(df: pd.DataFrame) -> str:
    for col in df.columns:
        normalized = str(col).replace("\n", " ").strip().lower()
        if "route type" in normalized:
            return col
    raise ValueError("Could not find the route type column in the CSV.")


def validate_columns(df: pd.DataFrame, route_type_col: str):
    required = [
        "S.No",
        "Route Name",
        route_type_col,
        "From Lat",
        "From Long",
        "To Lat",
        "To Long",
    ]
    missing = [c for c in required if c not in df.columns]
    if missing:
        raise ValueError(f"Missing required column(s): {missing}")


def get_road_coordinates(start_lat, start_lon, end_lat, end_lon, mode):
    profile = PROFILE_MAP.get(str(mode).strip(), "driving")
    url = (
        f"{OSRM_BASE_URL}/route/v1/{profile}/"
        f"{start_lon},{start_lat};{end_lon},{end_lat}"
        f"?overview=full&geometries=geojson"
    )

    try:
        response = requests.get(url, timeout=REQUEST_TIMEOUT)
        response.raise_for_status()
        data = response.json()

        if data.get("routes"):
            return data["routes"][0]["geometry"]["coordinates"], "osrm"
        else:
            print(f"No route found for mode={mode}. Using straight line fallback.")
    except Exception as e:
        print(f"Error fetching route for mode={mode}: {e}. Using fallback.")

    return [[start_lon, start_lat], [end_lon, end_lat]], "fallback"


def create_kml(df: pd.DataFrame, output_filename: str):
    route_type_col = detect_route_type_column(df)
    validate_columns(df, route_type_col)

    kml_parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<kml xmlns="http://www.opengis.net/kml/2.2">',
        "  <Document>",
        f"    <name>{escape(Path(output_filename).name)}</name>"
    ]

    # Shared styles
    for mode, color in COLOR_MAP.items():
        style_id = f"style_{mode.lower()}"
        kml_parts.extend([
            f'    <Style id="{style_id}">',
            "      <LineStyle>",
            f"        <color>{color}</color>",
            "        <width>4</width>",
            "      </LineStyle>",
            "      <IconStyle>",
            f"        <color>{color}</color>",
            "      </IconStyle>",
            "    </Style>",
        ])

    for _, row in df.iterrows():
        s_no = row["S.No"]
        route_name = str(row["Route Name"]).strip()
        mode = str(row[route_type_col]).strip()
        style_id = f"style_{mode.lower()}"
        color = COLOR_MAP.get(mode, "ffffffff")

        from_lat = float(row["From Lat"])
        from_lon = float(row["From Long"])
        to_lat = float(row["To Lat"])
        to_lon = float(row["To Long"])

        full_name = f"{s_no} - {route_name}"
        print(f"Processing {full_name} ({mode})...")

        road_points, source = get_road_coordinates(from_lat, from_lon, to_lat, to_lon, mode)
        coord_str = " ".join(f"{lon},{lat},0" for lon, lat in road_points)

        safe_name = escape(full_name)
        safe_mode = escape(mode)
        safe_source = escape(source)

        # Route line
        kml_parts.extend([
            "    <Placemark>",
            f"      <name>{safe_name}</name>",
            f"      <styleUrl>#{style_id}</styleUrl>",
            "      <description><![CDATA[",
            f"        Route Type: {safe_mode}<br/>",
            f"        Geometry Source: {safe_source}<br/>",
            "      ]]></description>",
            "      <LineString>",
            "        <tessellate>1</tessellate>",
            f"        <coordinates>{coord_str}</coordinates>",
            "      </LineString>",
            "    </Placemark>",
        ])

        # Start point
        kml_parts.extend([
            "    <Placemark>",
            f"      <name>{safe_name} - Start</name>",
            f"      <styleUrl>#{style_id}</styleUrl>",
            "      <Point>",
            f"        <coordinates>{from_lon},{from_lat},0</coordinates>",
            "      </Point>",
            "    </Placemark>",
        ])

        # End point
        kml_parts.extend([
            "    <Placemark>",
            f"      <name>{safe_name} - End</name>",
            f"      <styleUrl>#{style_id}</styleUrl>",
            "      <Point>",
            f"        <coordinates>{to_lon},{to_lat},0</coordinates>",
            "      </Point>",
            "    </Placemark>",
        ])

        time.sleep(SLEEP_BETWEEN_REQUESTS)

    kml_parts.extend([
        "  </Document>",
        "</kml>"
    ])

    with open(output_filename, "w", encoding="utf-8") as f:
        f.write("\n".join(kml_parts))

    print(f"Done! Created {output_filename}")


def main():
    input_path = Path(INPUT_FILE)
    if not input_path.exists():
        raise FileNotFoundError(f"Input file not found: {input_path.resolve()}")

    # encoding='latin1' helps if dash/special chars exist in the CSV
    df = pd.read_csv(input_path, encoding="latin1")
    create_kml(df, OUTPUT_FILE)


if __name__ == "__main__":
    main()
